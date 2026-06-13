namespace ServProgProject.Models
{
    // Models/Enums.cs
    public enum CellState { Empty, Player1Frog, Player2Frog }
    public enum GameStatus { WaitingForPlayers, InProgress, Finished }

    public static class BoardConstants
    {
        public const int Rows = 8, Cols = 8;
        public static bool IsWhite(int r, int c) => r >= 1 && r <= 6 && c >= 1 && c <= 6;
        public static bool IsSwamp(int r, int c) => !IsWhite(r, c);
    }
}
