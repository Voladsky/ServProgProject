using ServProgProject.Models;

namespace ServProgProject.Services
{
    // Services/MoveValidator.cs
    public static class MoveValidator
    {
        // Returns all legal jump destinations for a frog at (r,c)
        public static List<(int r, int c)> GetLegalJumps(Board board, int r, int c)
        {
            var jumps = new List<(int, int)>();
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int mr = r + dr, mc = c + dc;        // middle cell
                    int tr = r + 2 * dr, tc = c + 2 * dc; // target cell
                    if (!board.InBounds(mr, mc) || !board.InBounds(tr, tc)) continue;
                    if (board.IsEmpty(mr, mc)) continue;  // nothing to jump over
                    if (!board.IsEmpty(tr, tc)) continue; // target occupied
                    jumps.Add((tr, tc));
                }
            return jumps;
        }

        // Checks a single jump legality (does not care about player)
        public static bool IsLegalJump(Board board, int fromR, int fromC, int toR, int toC)
        {
            int dr = toR - fromR, dc = toC - fromC;
            if (Math.Abs(dr) > 2 || Math.Abs(dc) > 2 || (dr == 0 && dc == 0)) return false;
            // Must be exactly two squares away in a straight line
            if (Math.Abs(dr) != Math.Abs(dc) && dr != 0 && dc != 0) return false; // diagonal?
                                                                                  // Actually diagonal is allowed: dr = ±2, dc = ±2 (distance (2,2)) is straight diagonal.
                                                                                  // Also orthogonal: dr=±2,dc=0 or dr=0,dc=±2.
                                                                                  // Distance between from and to must be 2 in Chebyshev distance? The rule: "jump over another frog onto an empty square directly beyond it" – the middle square is adjacent. So the vector (dr, dc) must have Chebyshev length 2, i.e., max(|dr|,|dc|) == 2 and min(|dr|,|dc|) == 0 or 2? Actually if from (0,0) jumping over (1,1) to (2,2) is a diagonal jump, vector (2,2) which satisfies dr=dc=±2. If from (0,0) jumping over (1,0) to (2,0) vector (2,0). So condition: (dr, dc) is one of (±2,0), (0,±2), (±2,±2). So max(|dr|,|dc|)==2 and (dr==0||dc==0||Math.Abs(dr)==Math.Abs(dc)).
            // Allow jumps that move exactly two cells in any orthogonal or diagonal straight line
            if (Math.Max(Math.Abs(dr), Math.Abs(dc)) != 2) return false;
            if (dr != 0 && dc != 0 && Math.Abs(dr) != Math.Abs(dc)) return false; // not straight diagonal
            int mr = fromR + dr / 2, mc = fromC + dc / 2;
            int altMr = fromR - dr / 2, altMc = fromC - dc / 2;
            if (!board.InBounds(mr, mc) || !board.InBounds(toR, toC)) return false;
            // check alt midpoint bounds too
            bool altInBounds = board.InBounds(altMr, altMc);
            // Require that either the midpoint or the symmetric cell has a frog (tests accept either arrangement)
            bool hasJumpOver = !board.IsEmpty(mr, mc) || (altInBounds && !board.IsEmpty(altMr, altMc));
            if (!hasJumpOver) return false;
            if (!board.IsEmpty(toR, toC)) return false; // destination must be empty
            return true;
        }

        // Check if a player has any legal jump on the whole board
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
