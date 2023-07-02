using System;
using Godot;
using CG = System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

public partial class WFCTilesetAnalizer : Node2D
{
    const int LAYER = 0;

    [Export] private TileSet _tileSet;

    private CG.List<TileMap> _chunks = new CG.List<TileMap>();

    private CG.Dictionary<Vector2I, CG.HashSet<Vector2I>> tileMapPossiblilities =
        new CG.Dictionary<Vector2I, CG.HashSet<Vector2I>>();

    public override void _Ready()
    {
        base._Ready();
        var children = GetChildren();
        _chunks = children.Cast<TileMap>().ToList();

        Stopwatch watch = Stopwatch.StartNew();
        watch.Start();
        AnalizeTileChunks();
        GD.Print(tileMapPossiblilities.Count);
        GD.Print(watch.Elapsed);
        
        watch = Stopwatch.StartNew();
        watch.Start();
        AnalizeTileChunksAsync();
        GD.Print(watch.Elapsed);
        
        RunSamples();
    }
    
    static void RunSamples()
    {
        Stopwatch sw = Stopwatch.StartNew();
        var folder = System.IO.Directory.CreateDirectory("output");
        foreach (var file in folder.GetFiles()) file.Delete();

        Random random = new();
        XDocument xdoc = XDocument.Load("WFC/sample.xml");

        foreach (XElement xelem in xdoc.Root.Elements("overlapping", "simpletiled"))
        {
            Model model;
            string name = xelem.Get<string>("name");
            Console.WriteLine($"< {name}");

            bool isOverlapping = xelem.Name == "overlapping";
            int size = xelem.Get("size", isOverlapping ? 48 : 24);
            int width = xelem.Get("width", size);
            int height = xelem.Get("height", size);
            bool periodic = xelem.Get("periodic", false);
            string heuristicString = xelem.Get<string>("heuristic");
            var heuristic = heuristicString == "Scanline" ? Model.Heuristic.Scanline : (heuristicString == "MRV" ? Model.Heuristic.MRV : Model.Heuristic.Entropy);

            if (isOverlapping)
            {
                int N = xelem.Get("N", 3);
                bool periodicInput = xelem.Get("periodicInput", true);
                int symmetry = xelem.Get("symmetry", 8);
                bool ground = xelem.Get("ground", false);

                model = new OverlappingModel(name, N, width, height, periodicInput, periodic, symmetry, ground, heuristic);
            }
            else
            {
                string subset = xelem.Get<string>("subset");
                bool blackBackground = xelem.Get("blackBackground", false);

                model = new SimpleTiledModel(name, subset, width, height, periodic, blackBackground, heuristic);
            }

            for (int i = 0; i < xelem.Get("screenshots", 2); i++)
            {
                for (int k = 0; k < 10; k++)
                {
                    Console.Write("> ");
                    int seed = random.Next();
                    bool success = model.Run(seed, xelem.Get("limit", -1));
                    if (success)
                    {
                        Console.WriteLine("DONE with " + seed );
                        model.Save($"WFC/output/{name} {seed}.png");
                        if (model is SimpleTiledModel stmodel && xelem.Get("textOutput", false))
                            System.IO.File.WriteAllText($"WFC/output/{name} {seed}.txt", stmodel.TextOutput());
                        break;
                    }
                    else Console.WriteLine("CONTRADICTION");
                }
            }
        }

        Console.WriteLine($"time = {sw.ElapsedMilliseconds}");
    }
    

    private void AnalizeTileChunks()
    {
        foreach (var tm in _chunks)
        {
            // all possible positions in that tileset, so in a 3x3 tileMAP the usedCellCords is of size 9
            var usedCellCords = tm.GetUsedCells(LAYER);

            foreach (var tileCords in usedCellCords)
            {
                var cellIdentifier = tm.GetCellAtlasCoords(LAYER, tileCords);

                // gets all 4 neigbours of a rectangular cell, idsreagarding if that cell is a real tile or not
                var neighbours = tm.GetSurroundingCells(tileCords);
                // remove all neighbours that are not real tiles
                var validNeighbours = neighbours.Where(n => tm.GetCellSourceId(LAYER, n) != -1);
                // maps the cell tile to the atlas cords of the tile in the tileSET
                var atlasOfValidNeighbours = validNeighbours.Select(n => tm.GetCellAtlasCoords(LAYER, n)).ToHashSet();

                if (tileMapPossiblilities.Keys.Contains(cellIdentifier))
                {
                    tileMapPossiblilities[cellIdentifier] = tileMapPossiblilities[cellIdentifier]
                        .Union(atlasOfValidNeighbours).ToHashSet();
                }
                else
                {
                    tileMapPossiblilities.Add(cellIdentifier, atlasOfValidNeighbours);
                }
            }
        }
    }

    private async void AnalizeTileChunksAsync()
    {
        var result = new CG.Dictionary<Vector2I, CG.HashSet<Vector2I>>();
        
        CG.List<Task<CG.Dictionary<Vector2I, CG.HashSet<Vector2I>>>> tasks =
            new CG.List<Task<CG.Dictionary<Vector2I, CG.HashSet<Vector2I>>>>();

        foreach (var tm in _chunks)
        {
            tasks.Add(AnalyzeChunkAsync(tm));
        }

        CG.Dictionary<Vector2I, CG.HashSet<Vector2I>>[] taskResult = await Task.WhenAll(tasks);

        result = taskResult.SelectMany(dict => dict)
            .ToLookup(pair => pair.Key, pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.First());
        
        GD.Print(result.Count);
    }

    private async Task<CG.Dictionary<Vector2I, CG.HashSet<Vector2I>>>
        AnalyzeChunkAsync(TileMap tm)
    {
        CG.Dictionary<Vector2I, CG.HashSet<Vector2I>> possiblities =
            new CG.Dictionary<Vector2I, CG.HashSet<Vector2I>>();

        var usedCellCords = tm.GetUsedCells(LAYER);

        //HEAVY COMPUTING
        foreach (var tileCords in usedCellCords)
        {
            var cellIdentifier = tm.GetCellAtlasCoords(LAYER, tileCords);

            // gets all 4 neigbours of a rectangular cell, idsreagarding if that cell is a real tile or not
            var neighbours = tm.GetSurroundingCells(tileCords);
            // remove all neighbours that are not real tiles
            var validNeighbours = neighbours.Where(n => tm.GetCellSourceId(LAYER, n) != -1);
            // maps the cell tile to the atlas cords of the tile in the tileSET
            var atlasOfValidNeighbours = validNeighbours.Select(n => tm.GetCellAtlasCoords(LAYER, n)).ToHashSet();

            if (possiblities.Keys.Contains(cellIdentifier))
            {
                possiblities[cellIdentifier] = possiblities[cellIdentifier]
                    .Union(atlasOfValidNeighbours).ToHashSet();
            }
            else
            {
                possiblities.Add(cellIdentifier, atlasOfValidNeighbours);
            }
        }

        return possiblities;
    }


    public static string HashSetToString<T>(CG.HashSet<T> hashSet)
    {
        string s = "";
        foreach (var item in hashSet)
        {
            s += item + " ";
        }

        return s;
    }
}