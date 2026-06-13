using ServProgProject.Models;
using System.Collections.Generic;

namespace ServProgProject.Services
{
    public static class MoveValidator
    {
        // Returns all legal jump destinations for a frog at (r,c) according to Frog Chess rules.
        // Swamp destinations are only included if there exists at least one further legal jump from there.
        public static List<(int r, int c)> GetLegalJumps(Board board, int r, int c)
        {
            return GetLegalJumpsRecursive(board, r, c, new HashSet<(int, int)>());
        }

        private static List<(int r, int c)> GetLegalJumpsRecursive(Board board, int r, int c, HashSet<(int, int)> visited)
        {
            var jumps = new List<(int, int)>();
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int mr = r + dr, mc = c + dc;        // jumped-over cell
                    int tr = r + 2 * dr, tc = c + 2 * dc; // destination cell
                    if (!board.InBounds(mr, mc) || !board.InBounds(tr, tc)) continue;
                    if (board.IsEmpty(mr, mc)) continue;          // nothing to jump over
                    if (!board.IsEmpty(tr, tc)) continue;         // target occupied

                    bool isWhite = BoardConstants.IsWhite(tr, tc);
                    if (isWhite)
                    {
                        jumps.Add((tr, tc));
                    }
                    else
                    {
                        // Swamp destination – only legal if there is a continuation
                        var tempBoard = board.Clone();
                        bool isPlayer1 = board.IsPlayerFrog(r, c, true) || !board.IsPlayerFrog(r, c, false);
                        tempBoard.Remove(mr, mc);
                        tempBoard.Remove(r, c);
                        tempBoard.Place(tr, tc, isPlayer1);
                        var nextJumps = GetLegalJumpsRecursive(tempBoard, tr, tc, visited);
                        if (nextJumps.Count > 0)
                        {
                            jumps.Add((tr, tc));
                        }
                    }
                }
            }
            return jumps;
        }

        // Checks a single jump for basic legality (geometry and occupancy).
        // Does not check swamp continuation – that is handled by the chain validator in GameManager.
        public static bool IsLegalJump(Board board, int fromR, int fromC, int toR, int toC)
        {
            int dr = toR - fromR;
            int dc = toC - fromC;
            if (Math.Max(Math.Abs(dr), Math.Abs(dc)) != 2) return false;
            if (dr != 0 && dc != 0 && Math.Abs(dr) != Math.Abs(dc)) return false;

            int mr = fromR + dr / 2;
            int mc = fromC + dc / 2;

            if (!board.InBounds(mr, mc) || !board.InBounds(toR, toC)) return false;
            if (board.IsEmpty(mr, mc)) return false;
            if (!board.IsEmpty(toR, toC)) return false;
            return true;
        }

        // Checks if a player has any legal jump on the whole board (using the full rule).
        public static bool HasAnyLegalJump(Board board, bool isPlayer1)
        {
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (board.IsPlayerFrog(r, c, isPlayer1) && GetLegalJumps(board, r, c).Count > 0)
                        return true;
            return false;
        }
    }
}