namespace Microsoft.EntityFrameworkCore.Migrations;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class MigrationAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
