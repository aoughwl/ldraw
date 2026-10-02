namespace LDraw
{
    public struct LDrawBfcState
    {
        public bool Certified;
        public bool WindingCCW;
        public bool InvertNext;
        public bool Culling;

        public static LDrawBfcState Default => new LDrawBfcState
        {
            Certified = false,
            WindingCCW = true,
            InvertNext = false,
            Culling = true
        };

        /// <summary>
        /// Computes the child BFC state for a subfile reference.
        /// If the subfile's matrix has negative determinant, the winding is flipped.
        /// If InvertNext was set, the winding is also flipped.
        /// </summary>
        public LDrawBfcState ForChild(bool parentInvert, bool matrixNegDet)
        {
            bool invert = parentInvert ^ matrixNegDet;
            return new LDrawBfcState
            {
                Certified = Certified,
                WindingCCW = invert ? !WindingCCW : WindingCCW,
                InvertNext = false,
                Culling = Culling
            };
        }
    }
}
