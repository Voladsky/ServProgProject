using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ServProgProject.Models
{
    // Models/Board.cs
    public class Board
    {
        // Keep a 2D array for internal use and tests, but ignore it for JSON serialization
        [JsonIgnore]
        public CellState[,] Cells { get; private set; } = new CellState[8, 8];

        // Provide a jagged-array view that will be serialized as 'cells' for clients
        [JsonPropertyName("cells")]
        public CellState[][] CellsForJson
        {
            get
            {
                var arr = new CellState[8][];
                for (int r = 0; r < 8; r++)
                {
                    arr[r] = new CellState[8];
                    for (int c = 0; c < 8; c++) arr[r][c] = Cells[r, c];
                }
                return arr;
            }
            set
            {
                if (value == null) return;
                for (int r = 0; r < Math.Min(8, value.Length); r++)
                    for (int c = 0; c < Math.Min(8, value[r].Length); c++)
                        Cells[r, c] = value[r][c];
            }
        }

        public Board() { }

        // Deep copy
        public Board Clone()
        {
            var b = new Board();
            Array.Copy(Cells, b.Cells, Cells.Length);
            return b;
        }

        public bool IsEmpty(int r, int c) => Cells[r, c] == CellState.Empty;
        public bool IsPlayerFrog(int r, int c, bool isPlayer1) =>
            isPlayer1 ? Cells[r, c] == CellState.Player1Frog : Cells[r, c] == CellState.Player2Frog;

        public void Place(int r, int c, bool isPlayer1) =>
            Cells[r, c] = isPlayer1 ? CellState.Player1Frog : CellState.Player2Frog;

        public void Remove(int r, int c) => Cells[r, c] = CellState.Empty;

        public static IEnumerable<(int r, int c)> AllWhiteCells()
        {
            for (int r = 1; r <= 6; r++)
                for (int c = 1; c <= 6; c++)
                    yield return (r, c);
        }

        public bool InBounds(int r, int c) => r >= 0 && r < 8 && c >= 0 && c < 8;
    }
}
