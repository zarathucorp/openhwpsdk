namespace OpenHwp.Automation
{
    public struct HwpRect
    {
        public HwpRect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }

        public int Top { get; }

        public int Width { get; }

        public int Height { get; }

        public override string ToString()
        {
            return $"Left={Left}, Top={Top}, Width={Width}, Height={Height}";
        }
    }
}
