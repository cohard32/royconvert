namespace RoyConvert.App.Tests;

/// <summary>
/// Tests that change or read translated text. <see cref="Resources.Strings.Culture"/> is shared by the whole process,
/// so these must not run while another test switches it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiCultureCollection
{
    public const string Name = "UI culture";
}
