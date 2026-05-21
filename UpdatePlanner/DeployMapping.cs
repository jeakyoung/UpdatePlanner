namespace UpdatePlanner
{
    public enum MappingType { Folder, File }

    public class DeployMapping
    {
        public MappingType Type   { get; set; }
        public string      Source { get; set; }   // 폴더 경로 또는 파일 경로
        public string      Dest   { get; set; }   // 항상 대상 폴더

        public string TypeLabel => Type == MappingType.Folder ? "폴더" : "파일";
    }
}
