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
            var jumps = new List<(int, int)>();

            // Check all 8 directions for jumps
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;

                    // Only allow straight-line jumps (orthogonal or diagonal)
                    if (dr != 0 && dc != 0 && Math.Abs(dr) != Math.Abs(dc)) continue;

                    int mr = r + dr, mc = c + dc;        // jumped-over cell
                    int tr = r + 2 * dr, tc = c + 2 * dc; // destination cell

                    // Check bounds
                    if (!board.InBounds(mr, mc) || !board.InBounds(tr, tc)) continue;

                    // Check jump requirements: must jump over an occupied cell to an empty cell
                    if (board.IsEmpty(mr, mc)) continue;          // nothing to jump over
                    if (!board.IsEmpty(tr, tc)) continue;         // target occupied

                    // If destination is a white square, it's always legal
                    if (BoardConstants.IsWhite(tr, tc))
                    {
                        jumps.Add((tr, tc));
                    }
                    else
                    {
                        // Destination is in the swamp - only legal if there's at least one continuation jump
                        // We check this using the current board state (without modifying it)
                        // because we're just checking if a continuation *could* happen
                        var nextJumps = GetLegalJumpsFromSwamp(board, tr, tc, new HashSet<(int, int)>());
                        if (nextJumps.Count > 0)
                        {
                            jumps.Add((tr, tc));
                        }
                    }
                }
            }

            return jumps;
        }

        // Helper for checking jumps from swamp squares - only looks for jumps to white squares
        // as swamp->swamp jumps are only allowed if they lead back to white
        private static List<(int r, int c)> GetLegalJumpsFromSwamp(Board board, int r, int c, HashSet<(int, int)> visited)
        {
            var jumps = new List<(int, int)>();

            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    if (dr != 0 && dc != 0 && Math.Abs(dr) != Math.Abs(dc)) continue;

                    int mr = r + dr, mc = c + dc;        // jumped-over cell
                    int tr = r + 2 * dr, tc = c + 2 * dc; // destination cell

                    // Skip if already visited (to prevent infinite loops)
                    var pos = (tr, tc);
                    if (visited.Contains(pos)) continue;
                    visited.Add(pos);

                    // Check bounds
                    if (!board.InBounds(mr, mc) || !board.InBounds(tr, tc)) continue;

                    // Check jump requirements
                    if (board.IsEmpty(mr, mc)) continue;
                    if (!board.IsEmpty(tr, tc)) continue;

                    // From swamp, can only land on white squares, or continue to swamp then back to white
                    if (BoardConstants.IsWhite(tr, tc))
                    {
                        jumps.Add((tr, tc));
                    }
                    else
                    {
                        // Continue chain: swamp -> swamp -> white
                        var nextJumps = GetLegalJumpsFromSwamp(board, tr, tc, visited);
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

            // Must be exactly 2 squares away (orthogonal or diagonal)
            if (Math.Max(Math.Abs(dr), Math.Abs(dc)) != 2) return false;

            // Must be in a straight line (orthogonal or diagonal)
            if (dr != 0 && dc != 0 && Math.Abs(dr) != Math.Abs(dc)) return false;

            int mr = fromR + dr / 2;
            int mc = fromC + dc / 2;

            // Check bounds for jump-over and destination
            if (!board.InBounds(mr, mc) || !board.InBounds(toR, toC)) return false;

            // Must jump over an occupied cell to an empty cell
            if (board.IsEmpty(mr, mc)) return false;
            if (!board.IsEmpty(toR, toC)) return false;

            return true;
        }

        // Checks if a player has any legal jump on the whole board (using the full rule).
        public static bool HasAnyLegalJump(Board board, bool isPlayer1)
        {
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    if (board.IsPlayerFrog(r, c, isPlayer1) && GetLegalJumps(board, r, c).Count > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}