namespace WebServerPolygonSelector.Models;

public class TableConfig
{
    public string TableName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string GeometryColumn { get; set; } = string.Empty;
    public List<string> AttributeColumns { get; set; } = [];
}
