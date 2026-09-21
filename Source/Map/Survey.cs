using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Server;
using RimWorld;
using Verse;

namespace RimBridge.MapView
{
    /// <summary>
    /// map.survey: the whole map (or a region) in one token-cheap call.
    /// Two layers: a run-length-encoded base grid of *spatially coherent* classes (terrain, rock, water, zones,
    /// structure footprints), plus sparse lists that carry the detail a grid cannot (defName, rotation, state,
    /// stack counts) in a tiny position grammar. Pawns and grass never enter the grid, so runs stay long.
    ///
    /// Position grammar (used everywhere in the result):
    ///   "x,z"          one cell            "x1-x2,z"  horizontal run     "x,z1-z2"  vertical run
    ///   "x1-x2,z1-z2"  rectangle           trailing N/E/S/W = rotation   trailing * = forbidden
    /// Grid grammar: rows top (max z) to bottom; "z:" or "zTop-zBottom:" when consecutive rows are identical;
    /// each run is [count]glyph, count omitted when 1. Glyphs are never digits.
    /// </summary>
    public static class Survey
    {
        public const string Legend =
            "? fog | # wall | + door | B other building | b blueprint/frame | _ stockpile | , growing zone | " +
            "^ rock | o ore | ~ water | T tree | - floor/road | f fertile soil | : sand/gravel | . ground";

        public const string Grammar =
            "positions: 'x,z' cell, 'x1-x2,z' horizontal run, 'x,z1-z2' vertical run, 'x1-x2,z1-z2' rect; " +
            "trailing N/E/S/W = rotation (pass it to ui.build), trailing * = forbidden. " +
            "grid: rows top (max z) to bottom, 'z:' or 'zTop-zBottom:' when rows repeat, runs are [count]glyph, " +
            "col c of a row covers cells x = x0 + c*scale .. x0 + (c+1)*scale - 1.";

        static readonly string[] Sections = { "grid", "buildings", "blueprints", "zones", "roof", "pawns", "items", "designations", "home" };

        [Rpc("map.survey", "{rect?: location-rect | x?, z?, w?, h?, center?: bool | around?: thingId|pawn|anchor|Room:N (whole map when omitted), scale?: cells per glyph (auto: fits ~64 cols), budget?: 6000 chars (grid coarsens and lists shorten to fit), format?: auto|rle|rows, only?: [grid|buildings|blueprints|zones|roof|pawns|items|designations|home], trees?: true, power?: false (list conduits), xray?: false (see through fog: ore, ruin interiors; marks the game assisted)} " +
             "SEE THE WHOLE MAP CHEAPLY: base grid (terrain/rock/water/zones/structure footprints; run-length encoded when that is cheaper, else aligned rows with an x ruler) + sparse lists with exact cells: walls as segments, buildings by def with position+rotation, blueprints/frames, zones as rects, constructed-roof rects, pawns with facing, item stacks by def, designations. Use this first, then map.detail to zoom.")]
        public static JToken Run(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var map = Find.CurrentMap;
            var box = ResolveBox(p, map);
            int budget = Math.Max(1000, P.Int(p, "budget", 6000));
            bool trees = P.Bool(p, "trees", true), power = P.Bool(p, "power", false), xray = P.Bool(p, "xray", false);
            // Seeing through fog is a cheat, so it follows the dev.* convention: the game is marked assisted in the ledger.
            if (xray && !Ledger.EventLedger.Assisted) { Ledger.EventLedger.Assisted = true; Ledger.EventLedger.Add("assisted", "map.survey xray used; this game is now marked assisted"); }
            var only = new HashSet<string>(Sections);
            if (p["only"] is JArray oa)
            {
                only.Clear();
                // Name the element that was rejected. "only must be a list of grid|buildings|..." does not say which
                // of the three sent was the bad one, and the caller had sent two valid names and one invented one.
                foreach (var s in oa)
                {
                    var name = Server.P.Flat(s);
                    if (!Sections.Contains(name)) throw new RpcError($"only: '{name}' is not a section. Available: {string.Join("|", Sections)}");
                    only.Add(name);
                }
            }

            // The grid is sized first (it may coarsen to fit); the sparse lists then get whatever is left, and
            // their per-list caps tighten tier by tier until the whole payload fits the budget (the agent
            // truncates tool results at ~8k chars, so an over-budget answer would lose its tail silently).
            string? grid = null; string format = P.Str(p, "format", "auto"); int scale = 0; var counts = new JObject();
            if (format != "auto" && format != "rle" && format != "rows") throw new RpcError("format must be auto|rle|rows");
            if (only.Contains("grid"))
            {
                var cls = Classify(map, box, trees, xray, counts);
                scale = P.Int(p, "scale", 0);
                bool auto = scale <= 0;
                if (auto) scale = Math.Max(1, (int)Math.Ceiling(Math.Max(box.Width, box.Height) / 64.0));
                // Measured with o200k/Qwen tokenizers: a raw row costs ~0.27 tok/char (long same-glyph runs merge into
                // single tokens) while RLE costs ~0.8 tok/char (digit/letter mixes), so RLE only wins on tokens when it
                // is under a third of the raw length. Raw rows are also easier for a model to read spatially, so they
                // are preferred whenever they fit the char budget and RLE would not beat them.
                while (true)
                {
                    var (rle, rows) = Encode(cls, box, scale);
                    bool useRle = format == "rle" || (format == "auto" && (rle.Length * 4 < rows.Length || rows.Length > budget / 2));
                    grid = useRle ? rle : rows;
                    if (!auto || grid.Length <= budget / 2 || scale >= 16) { format = useRle ? "rle" : "rows"; break; }
                    scale++;
                }
            }
            JObject res = new JObject();
            for (int tier = 0; tier < Caps.Tiers.Length; tier++)
            {
                var caps = Caps.Tiers[tier];
                res = new JObject { ["box"] = Render.Value(box, 1), ["legend"] = Legend, ["grammar"] = Grammar };
                if (grid != null)
                {
                    res["scale"] = scale; res["format"] = format; res["x0"] = box.minX; res["z_top"] = box.maxZ;
                    res["cols"] = (box.Width + scale - 1) / scale; res["rows"] = (box.Height + scale - 1) / scale;
                    res["grid"] = grid;
                }
                if (only.Contains("buildings")) { res["buildings"] = Buildings(map, box, power, playerOnly: true, counts, caps.PerDef, xray); res["other_buildings"] = Buildings(map, box, power, playerOnly: false, counts, caps.OtherPerDef, xray); }
                if (only.Contains("blueprints")) { res["blueprints"] = Planned(map, box, frames: false, counts, caps.PerDef); res["frames"] = Planned(map, box, frames: true, counts, caps.PerDef); }
                if (only.Contains("zones")) res["zones"] = Zones(map, box, caps.Rects);
                if (only.Contains("roof")) res["roof_built"] = RoofRects(map, box, counts, caps.Rects);
                if (only.Contains("home")) res["home"] = Home(map, box);
                if (only.Contains("pawns")) res["pawns"] = Pawns(map, box, caps.WildPos);
                if (only.Contains("items")) res["items"] = Items(map, box, counts, caps.ItemKinds, caps.ItemPos, xray);
                if (xray) res["xray"] = true;
                if (only.Contains("designations")) res["designations"] = Designations(map, box, caps.PerDef);
                var anchors = new JObject();
                foreach (var kv in AnchorComponent.All()) if (kv.Value.Overlaps(box)) anchors[kv.Key] = Span(kv.Value);
                if (anchors.Count > 0) res["anchors"] = anchors;
                res["counts"] = counts;
                if (tier > 0) res["trimmed"] = "lists shortened to fit budget " + budget + " (tier " + tier + "); survey a smaller rect or raise budget for full lists";
                if (res.ToString(Newtonsoft.Json.Formatting.None).Length <= budget) break;
            }
            return res;
        }

        /// <summary>List caps per budget tier; tier 0 is generous, later tiers keep only the gist.</summary>
        sealed class Caps
        {
            public int PerDef, OtherPerDef, Rects, ItemKinds, ItemPos, WildPos;
            public static readonly Caps[] Tiers =
            {
                new Caps { PerDef = 40, OtherPerDef = 12, Rects = 30, ItemKinds = 40, ItemPos = 8, WildPos = 3 },
                new Caps { PerDef = 24, OtherPerDef = 6, Rects = 16, ItemKinds = 24, ItemPos = 4, WildPos = 2 },
                new Caps { PerDef = 12, OtherPerDef = 3, Rects = 8, ItemKinds = 12, ItemPos = 2, WildPos = 1 },
                new Caps { PerDef = 6, OtherPerDef = 1, Rects = 4, ItemKinds = 6, ItemPos = 1, WildPos = 0 },
            };
        }

        // ---------------------------------------------------------------- region

        static CellRect ResolveBox(JObject p, Map map)
        {
            CellRect box;
            if (p["rect"] != null) box = Locate.Rect(p["rect"], map, "rect");
            else if (p["around"] != null)
            {
                var t = Lookup.ThingOrNull(P.Str(p, "around")) ?? Lookup.PawnOrNull(P.Str(p, "around"));
                var c = t?.PositionHeld ?? Locate.Rect(p["around"], map, "around").CenterCell;
                box = CellRect.CenteredOn(c, P.Int(p, "w", 60), P.Int(p, "h", 60));
            }
            else if (p["x"] != null || p["w"] != null)
            {
                var home = State.Snapshot.HomeCenter(map);
                int w = P.Int(p, "w", 60), h = P.Int(p, "h", w);
                int x = P.Int(p, "x", home.x), z = P.Int(p, "z", home.z);
                box = P.Bool(p, "center", p["x"] == null) ? CellRect.CenteredOn(new IntVec3(x, 0, z), w, h) : new CellRect(x, z, w, h);
            }
            else box = CellRect.WholeMap(map);
            box = box.ClipInsideMap(map);
            if (box.Area == 0) throw new RpcError("rect is empty or off the map");
            return box;
        }

        // ---------------------------------------------------------------- base grid

        /// <summary>One class glyph per cell, row-major from box.minX/minZ.</summary>
        static char[] Classify(Map map, CellRect box, bool trees, bool xray, JObject counts)
        {
            var cls = new char[box.Width * box.Height];
            int fog = 0, rock = 0, water = 0, fertile = 0, tree = 0, ore = 0;
            for (int z = box.minZ; z <= box.maxZ; z++)
            for (int x = box.minX; x <= box.maxX; x++)
            {
                var c = new IntVec3(x, 0, z);
                char g = CellGlyph(map, c, trees, xray);
                switch (g) { case '?': fog++; break; case '^': rock++; break; case '~': water++; break; case 'f': fertile++; break; case 'T': tree++; break; case 'o': ore++; break; }
                cls[(z - box.minZ) * box.Width + (x - box.minX)] = g;
            }
            int n = cls.Length;
            counts["cells"] = n; counts["fog_pct"] = 100 * fog / n; counts["rock_pct"] = 100 * rock / n; counts["water_pct"] = 100 * water / n;
            counts["fertile_pct"] = 100 * fertile / n; counts["trees"] = tree; counts["ore_cells"] = ore;
            return cls;
        }

        static char CellGlyph(Map map, IntVec3 c, bool trees, bool xray)
        {
            if (!xray && c.Fogged(map)) return '?';
            var ed = c.GetEdifice(map);
            if (ed != null)
            {
                var d = ed.def;
                if (d.building != null && d.building.isResourceRock) return 'o';
                if (d.building != null && d.building.isNaturalRock) return '^';
                if (ed is Building_Door) return '+';
                if (IsWallLike(d)) return '#';
                return 'B';
            }
            var list = c.GetThingList(map);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t is Blueprint || t is Frame) return 'b';
                if (t.def.category == ThingCategory.Building && !t.def.defName.Contains("Conduit")) return 'B';
            }
            if (trees && c.GetPlant(map) is Plant pl && pl.def.plant.IsTree) return 'T';
            var zone = c.GetZone(map);
            if (zone is Zone_Stockpile) return '_';
            if (zone is Zone_Growing) return ',';
            var terr = c.GetTerrain(map);
            if (terr.IsWater) return '~';
            if (terr.passability == Traversability.Impassable) return '^';
            if (terr.layerable || terr.bridge || terr.IsFloor || terr.IsRoad) return '-';
            if (terr.fertility >= 1f) return 'f';
            if (terr.fertility > 0f && terr.fertility < 1f) return ':';
            return '.';
        }

        static bool IsWallLike(ThingDef d) =>
            d.IsSmoothed || d.IsSmoothable || d.defName.Contains("Wall") ||
            (d.fillPercent >= 1f && d.building != null && !d.building.isTrap && d.passability == Traversability.Impassable && d.size == IntVec2.One && d.category == ThingCategory.Building && !typeof(Building_Turret).IsAssignableFrom(d.thingClass));

        /// <summary>Downsample by 'scale' (majority with structure/zone priority), then encode both ways:
        /// RLE (identical rows collapse to "zTop-zBottom:") and raw rows with an x ruler (one glyph per column).</summary>
        static (string rle, string rows) Encode(char[] cls, CellRect box, int scale)
        {
            int w = box.Width, h = box.Height;
            int cols = (w + scale - 1) / scale, rows = (h + scale - 1) / scale;
            var tally = new Dictionary<char, int>();
            var block = new char[rows * cols];
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (scale == 1) { block[r * cols + c] = cls[r * w + c]; continue; }
                tally.Clear();
                for (int dz = 0; dz < scale; dz++)
                for (int dx = 0; dx < scale; dx++)
                {
                    int x = c * scale + dx, z = r * scale + dz;
                    if (x >= w || z >= h) continue;
                    char k = cls[z * w + x];
                    tally[k] = tally.TryGetValue(k, out var n) ? n + 1 : 1;
                }
                block[r * cols + c] = BlockGlyph(tally);
            }

            // raw rows: ruler of column index (hundreds/tens/units), then "z  glyphs" top-down; x = x0 + col*scale
            var raw = new StringBuilder();
            if (cols > 100) { raw.Append("     "); for (int c = 0; c < cols; c++) raw.Append(c >= 100 ? (c / 100).ToString() : " "); raw.Append('\n'); }
            raw.Append("     "); for (int c = 0; c < cols; c++) raw.Append(c % 10 == 0 ? (c / 10 % 10).ToString() : " "); raw.Append('\n');
            raw.Append("     "); for (int c = 0; c < cols; c++) raw.Append((c % 10).ToString()); raw.Append('\n');

            var rle = new StringBuilder();
            string? prev = null; int prevTop = 0, prevBottom = 0;
            void Flush()
            {
                if (prev == null) return;
                rle.Append(prevTop == prevBottom ? prevTop.ToString() : prevTop + "-" + prevBottom).Append(':').Append(prev).Append('\n');
            }
            for (int r = rows - 1; r >= 0; r--)
            {
                int zTop = box.minZ + Math.Min(h - 1, r * scale + scale - 1), zBottom = box.minZ + r * scale;
                raw.Append(zTop.ToString().PadLeft(4)).Append(' ').Append(block, r * cols, cols).Append('\n');
                var row = new StringBuilder();
                char run = '\0'; int len = 0;
                for (int c = 0; c < cols; c++)
                {
                    char g = block[r * cols + c];
                    if (g == run) len++;
                    else { if (len > 0) { if (len > 1) row.Append(len); row.Append(run); } run = g; len = 1; }
                }
                if (len > 1) row.Append(len); row.Append(run);
                string s = row.ToString();
                if (s == prev) prevBottom = zBottom;
                else { Flush(); prev = s; prevTop = zTop; prevBottom = zBottom; }
            }
            Flush();
            return (rle.ToString(), raw.ToString());
        }

        static char BlockGlyph(Dictionary<char, int> tally)
        {
            int total = tally.Values.Sum();
            int Get(char k) => tally.TryGetValue(k, out var n) ? n : 0;
            if (Get('?') * 2 >= total) return '?';
            int walls = Get('#') + Get('+'), other = Get('B');
            if (walls + other > 0) return walls >= other ? '#' : 'B';
            if (Get('b') > 0) return 'b';
            if (Get('_') * 4 >= total) return '_';
            if (Get(',') * 4 >= total) return ',';
            if (Get('o') * 6 >= total) return 'o';
            if (Get('T') * 3 >= total) return 'T'; // a third trees is a forest for building purposes
            // majority of the rest, ties broken by priority order
            const string prio = "^~T-f:._,?ob";
            char best = '.'; int bestN = -1;
            foreach (var kv in tally)
            {
                if (kv.Value > bestN || (kv.Value == bestN && prio.IndexOf(kv.Key) < prio.IndexOf(best))) { best = kv.Key; bestN = kv.Value; }
            }
            return best;
        }

        // ---------------------------------------------------------------- sparse lists

        static string Span(int x1, int x2, int z1, int z2)
        {
            string xs = x1 == x2 ? x1.ToString() : x1 + "-" + x2, zs = z1 == z2 ? z1.ToString() : z1 + "-" + z2;
            return xs + "," + zs;
        }
        static string Span(CellRect r) => Span(r.minX, r.maxX, r.minZ, r.maxZ);
        static string Pos(IntVec3 c) => c.x + "," + c.z;

        /// <summary>Cover a cell set with horizontal/vertical runs (walls, conduits, designations).</summary>
        static JArray Segments(HashSet<IntVec3> cells, int limit, out int extra)
        {
            var arr = new JArray(); extra = 0;
            var done = new HashSet<IntVec3>();
            foreach (var c in cells.OrderBy(k => k.z).ThenBy(k => k.x))
            {
                if (done.Contains(c)) continue;
                int hl = 1; while (cells.Contains(new IntVec3(c.x + hl, 0, c.z)) && !done.Contains(new IntVec3(c.x + hl, 0, c.z))) hl++;
                int vl = 1; while (cells.Contains(new IntVec3(c.x, 0, c.z + vl)) && !done.Contains(new IntVec3(c.x, 0, c.z + vl))) vl++;
                string s;
                if (hl >= vl && hl > 1) { for (int i = 0; i < hl; i++) done.Add(new IntVec3(c.x + i, 0, c.z)); s = Span(c.x, c.x + hl - 1, c.z, c.z); }
                else if (vl > 1) { for (int i = 0; i < vl; i++) done.Add(new IntVec3(c.x, 0, c.z + i)); s = Span(c.x, c.x, c.z, c.z + vl - 1); }
                else { done.Add(c); s = Pos(c); }
                if (arr.Count < limit) arr.Add(s); else extra++;
            }
            return arr;
        }

        /// <summary>Greedy rectangle cover of a cell set (zones, roofs).</summary>
        static JArray Rects(HashSet<IntVec3> cells, int limit, out int extra)
        {
            var arr = new JArray(); extra = 0;
            var done = new HashSet<IntVec3>();
            bool Free(int x, int z) { var c = new IntVec3(x, 0, z); return cells.Contains(c) && !done.Contains(c); }
            foreach (var c in cells.OrderByDescending(k => k.z).ThenBy(k => k.x))
            {
                if (done.Contains(c)) continue;
                int w = 1; while (Free(c.x + w, c.z)) w++;
                int h = 1;
                while (true)
                {
                    bool ok = true;
                    for (int i = 0; i < w && ok; i++) if (!Free(c.x + i, c.z - h)) ok = false;
                    if (!ok) break; h++;
                }
                for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) done.Add(new IntVec3(c.x + i, 0, c.z - j));
                if (arr.Count < limit) arr.Add(Span(c.x, c.x + w - 1, c.z - h + 1, c.z)); else extra++;
            }
            return arr;
        }

        static string Key(BuildableDef def)
        {
            var size = def is ThingDef td ? td.size : IntVec2.One;
            return def.defName + "(" + size.x + "x" + size.z + ")";
        }

        static bool Rotatable(BuildableDef def) => def is ThingDef td && td.rotatable && !(td.size.x == 1 && td.size.z == 1 && !td.hasInteractionCell && td.graphicData?.drawRotated == false);

        static string Rot(Thing t) => t.Rotation.ToStringWord().Substring(0, 1);

        /// <summary>Group things by def; non-rotatable 1x1 kinds compress to segments, everything else is "x,z" + rotation.</summary>
        static JObject Grouped(IEnumerable<(Thing t, BuildableDef def)> things, int perDef)
        {
            var groups = new Dictionary<string, (BuildableDef def, List<Thing> list)>();
            foreach (var (t, def) in things)
            {
                string k = Key(def);
                if (!groups.TryGetValue(k, out var g)) { g = (def, new List<Thing>()); groups[k] = g; }
                g.list.Add(t);
            }
            var o = new JObject();
            foreach (var kv in groups.OrderByDescending(g => g.Value.list.Count))
            {
                var (def, list) = kv.Value;
                bool oneByOne = !(def is ThingDef td) || (td.size.x == 1 && td.size.z == 1);
                JArray arr; int extra;
                if (oneByOne && !Rotatable(def))
                    arr = Segments(new HashSet<IntVec3>(list.Select(t => t.Position)), perDef, out extra);
                else
                {
                    arr = new JArray(); extra = 0;
                    foreach (var t in list.OrderBy(t => t.Position.z).ThenBy(t => t.Position.x))
                    {
                        if (arr.Count >= perDef) { extra++; continue; }
                        string s = Pos(t.Position);
                        if (Rotatable(def)) s += Rot(t);
                        if (t.IsForbidden(Faction.OfPlayer)) s += "*";
                        arr.Add(s);
                    }
                }
                if (extra > 0) arr.Add("+" + extra + " more");
                o[kv.Key + " x" + list.Count] = arr;
            }
            return o;
        }

        static JObject Buildings(Map map, CellRect box, bool power, bool playerOnly, JObject counts, int perDef, bool xray)
        {
            var src = new List<(Thing, BuildableDef)>();
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (!t.Spawned || !box.Contains(t.Position) || (!xray && t.Position.Fogged(map))) continue;
                if ((t.Faction == Faction.OfPlayer) != playerOnly) continue;
                if (!power && t.def.defName.Contains("Conduit")) continue;
                src.Add((t, t.def));
            }
            if (playerOnly) counts["buildings"] = src.Count; else counts["other_buildings"] = src.Count;
            return Grouped(src, perDef);
        }

        static JObject Planned(Map map, CellRect box, bool frames, JObject counts, int perDef)
        {
            var src = new List<(Thing, BuildableDef)>();
            foreach (var t in map.listerThings.ThingsInGroup(frames ? ThingRequestGroup.BuildingFrame : ThingRequestGroup.Blueprint))
            {
                if (!t.Spawned || !box.Contains(t.Position)) continue;
                var def = (t as Blueprint)?.def.entityDefToBuild ?? (t as Frame)?.def.entityDefToBuild;
                if (def == null) continue;
                src.Add((t, def));
            }
            counts[frames ? "frames" : "blueprints"] = src.Count;
            return Grouped(src, perDef);
        }

        static JArray Zones(Map map, CellRect box, int maxRects)
        {
            var arr = new JArray();
            foreach (var z in map.zoneManager.AllZones)
            {
                var cells = new HashSet<IntVec3>(z.Cells.Where(c => box.Contains(c)));
                if (cells.Count == 0) continue;
                var o = new JObject { ["label"] = z.label, ["type"] = z is Zone_Stockpile ? "stockpile" : z is Zone_Growing ? "growing" : z.GetType().Name, ["cells"] = z.Cells.Count };
                o["rects"] = Rects(cells, Math.Min(12, maxRects), out int extra);
                if (extra > 0) o["more_rects"] = extra;
                if (z is Zone_Growing zg) { o["plant"] = zg.GetPlantDefToGrow()?.defName; if (!zg.allowSow) o["sowing"] = false; }
                if (z is Zone_Stockpile zs) o["priority"] = zs.settings.Priority.ToString();
                arr.Add(o);
            }
            return arr;
        }

        static JToken RoofRects(Map map, CellRect box, JObject counts, int maxRects)
        {
            var built = new HashSet<IntVec3>(); int natural = 0;
            foreach (var c in box)
            {
                var r = c.GetRoof(map);
                if (r == null) continue;
                if (r.isNatural) natural++; else built.Add(c);
            }
            counts["roof_built_cells"] = built.Count; counts["roof_natural_cells"] = natural;
            var arr = Rects(built, maxRects, out int extra);
            if (extra > 0) arr.Add("+" + extra + " more");
            return arr;
        }

        static JToken Home(Map map, CellRect box)
        {
            var home = map.areaManager.Home;
            if (home == null) return JValue.CreateNull();
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue, n = 0;
            foreach (var c in home.ActiveCells)
            {
                if (!box.Contains(c)) continue; n++;
                if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x; if (c.z < minZ) minZ = c.z; if (c.z > maxZ) maxZ = c.z;
            }
            if (n == 0) return new JObject { ["cells"] = 0 };
            return new JObject { ["cells"] = n, ["bounds"] = Span(minX, maxX, minZ, maxZ) };
        }

        /// <summary>Colonists, colony animals and hostiles one per line with facing; wild animals and other factions'
        /// pawns grouped by kind (a whole map holds dozens of wild animals; the model wants "8 muffalo around 152,165").</summary>
        static JObject Pawns(Map map, CellRect box, int wildPos)
        {
            var colonists = new JArray(); var animals = new JArray(); var hostiles = new JArray();
            var wild = new Dictionary<string, List<Pawn>>();
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (!box.Contains(p.Position)) continue;
                var sb = new StringBuilder();
                sb.Append(p.LabelShort).Append(' ').Append(p.ThingID).Append(' ').Append(Pos(p.Position)).Append(Rot(p));
                if (p.Dead) sb.Append(" dead"); else if (p.Downed) sb.Append(" downed");
                if (p.Drafted) sb.Append(" drafted");
                if (p.HostileTo(Faction.OfPlayer)) { sb.Append(' ').Append(p.kindDef?.defName); if (p.Faction != null) sb.Append(' ').Append(p.Faction.Name); hostiles.Add(sb.ToString()); }
                else if (p.IsColonist)
                {
                    string job = p.jobs?.curDriver != null ? p.jobs.curDriver.GetReport().StripTags() : "";
                    if (job.Length > 0) sb.Append(": ").Append(State.Snapshot.Trunc(job, 48));
                    colonists.Add(sb.ToString());
                }
                else if (p.Faction == Faction.OfPlayer) { sb.Append(' ').Append(p.kindDef?.defName); animals.Add(sb.ToString()); }
                else
                {
                    string k = (p.kindDef?.defName ?? p.def.defName) + (p.Faction != null ? " (" + p.Faction.Name + ")" : "");
                    if (!wild.TryGetValue(k, out var l)) wild[k] = l = new List<Pawn>();
                    l.Add(p);
                }
            }
            var others = new JObject();
            foreach (var kv in wild.OrderByDescending(k => k.Value.Count))
            {
                var l = kv.Value;
                var sb = new StringBuilder();
                sb.Append(l.Count);
                if (wildPos > 0) { sb.Append(" @"); foreach (var p in l.Take(wildPos)) sb.Append(' ').Append(Pos(p.Position)); if (l.Count > wildPos) sb.Append(" +").Append(l.Count - wildPos); }
                others[kv.Key] = sb.ToString();
            }
            return new JObject { ["colonists"] = colonists, ["animals"] = animals, ["hostiles"] = hostiles, ["others"] = others };
        }

        static JObject Items(Map map, CellRect box, JObject counts, int maxKinds, int maxPos, bool xray)
        {
            var groups = new Dictionary<string, List<Thing>>();
            int stacks = 0;
            foreach (var t in map.listerThings.AllThings)
            {
                if (t.def.category != ThingCategory.Item || !t.Spawned || !box.Contains(t.Position) || (!xray && t.Position.Fogged(map))) continue;
                stacks++;
                if (!groups.TryGetValue(t.def.defName, out var l)) groups[t.def.defName] = l = new List<Thing>();
                l.Add(t);
            }
            counts["item_stacks"] = stacks;
            var o = new JObject();
            int g = 0;
            foreach (var kv in groups.OrderByDescending(k => k.Value.Sum(t => t.stackCount)))
            {
                if (g++ >= maxKinds) { o["..."] = (groups.Count - maxKinds) + " more kinds"; break; }
                var list = kv.Value;
                var sb = new StringBuilder();
                sb.Append(list.Sum(t => t.stackCount));
                if (list.Count > 1) sb.Append(" in ").Append(list.Count).Append(" stacks");
                sb.Append(" @");
                int i = 0;
                foreach (var t in list.OrderBy(t => t.Position.z).ThenBy(t => t.Position.x))
                {
                    if (i++ >= maxPos) { sb.Append(" +").Append(list.Count - maxPos).Append(" more"); break; }
                    sb.Append(' ').Append(Pos(t.Position));
                    if (t.IsForbidden(Faction.OfPlayer)) sb.Append('*');
                }
                o[kv.Key] = sb.ToString();
            }
            return o;
        }

        static JObject Designations(Map map, CellRect box, int perDef)
        {
            var byDef = new Dictionary<string, HashSet<IntVec3>>();
            foreach (var d in map.designationManager.AllDesignations)
            {
                var c = d.target.HasThing ? d.target.Thing.Position : d.target.Cell;
                if (!c.IsValid || !box.Contains(c)) continue;
                if (!byDef.TryGetValue(d.def.defName, out var set)) byDef[d.def.defName] = set = new HashSet<IntVec3>();
                set.Add(c);
            }
            var o = new JObject();
            foreach (var kv in byDef.OrderByDescending(k => k.Value.Count))
            {
                var arr = Segments(kv.Value, perDef, out int extra);
                if (extra > 0) arr.Add("+" + extra + " more");
                o[kv.Key + " x" + kv.Value.Count] = arr;
            }
            return o;
        }
    }
}
