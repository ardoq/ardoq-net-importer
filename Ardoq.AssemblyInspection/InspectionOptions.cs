namespace Ardoq.AssemblyInspection
{
    public class InspectionOptions
    {
        public bool SelfReference { get; set; }
        public bool IncludePrivateMethods { get; set; }
        public bool IncludeInstructionReferences { get; set; }
        public bool SkipAddMethodsToDocs { get; set; }
        public bool SkipExternalAssemblyDetails { get; set; }
    }
}