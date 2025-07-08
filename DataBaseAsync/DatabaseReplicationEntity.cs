namespace DatabaseReplication
{
    // 示例实体类
    public class Product
    {
        public int ProductId { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public DateTime LastUpdated { get; set; }
        public bool IsActive { get; set; }
    }

    public class UserPreference
    {
        public int UserId { get; set; }
        public string Theme { get; set; }
        public string Language { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class AppSetting
    {
        public int SettingId { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public DateTime UpdateTime { get; set; }
        public int Version { get; set; }
    }
}