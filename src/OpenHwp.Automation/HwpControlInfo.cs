namespace OpenHwp.Automation
{
    public sealed class HwpControlInfo
    {
        public HwpControlInfo(int index, string ctrlId, int typeIndex)
        {
            Index = index;
            CtrlId = ctrlId ?? string.Empty;
            TypeIndex = typeIndex;
        }

        public int Index { get; private set; }

        public string CtrlId { get; private set; }

        public int TypeIndex { get; private set; }
    }
}
