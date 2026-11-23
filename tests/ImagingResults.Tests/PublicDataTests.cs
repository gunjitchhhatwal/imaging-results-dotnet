using FellowOakDicom;

namespace ImagingResults.Tests;

public sealed class PublicDataTests
{
    private const string PublishedPatientId = "IMAGINGRESULTS-DEMO-001";

    [Fact]
    public void BundledTciaDataMatchesPublicationProfile()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dataDirectory = Path.Combine(
            repositoryRoot,
            "examples",
            "demo-data",
            "tcia-tcga-bp-4343");
        var paths = Directory.GetFiles(dataDirectory, "*.dcm", SearchOption.AllDirectories);

        Assert.Equal(102, paths.Length);
        foreach (var path in paths)
        {
            var dataset = DicomFile.Open(path).Dataset;
            AssertPublicationProfile(dataset);
            AssertNoPrivateElements(dataset);
        }
    }

    private static void AssertPublicationProfile(DicomDataset dataset)
    {
        Assert.Equal(PublishedPatientId, dataset.GetSingleValue<string>(DicomTag.PatientID));
        Assert.Equal("YES", dataset.GetSingleValue<string>(DicomTag.PatientIdentityRemoved));
        Assert.NotEqual("YES", dataset.GetSingleValueOrDefault(DicomTag.BurnedInAnnotation, string.Empty));

        Assert.Empty(dataset.GetSingleValueOrDefault(DicomTag.AccessionNumber, string.Empty));
        Assert.Empty(dataset.GetSingleValueOrDefault(DicomTag.PatientBirthDate, string.Empty));
        Assert.Empty(dataset.GetSingleValueOrDefault(DicomTag.PatientSex, string.Empty));
        Assert.Empty(dataset.GetSingleValueOrDefault(DicomTag.PatientAge, string.Empty));
        Assert.Empty(dataset.GetSingleValueOrDefault(DicomTag.DeviceSerialNumber, string.Empty));
        Assert.Empty(dataset.GetSingleValueOrDefault(DicomTag.InstitutionName, string.Empty));
    }

    private static void AssertNoPrivateElements(DicomDataset dataset)
    {
        Assert.DoesNotContain(dataset, item => item.Tag.IsPrivate);
        foreach (var sequence in dataset.OfType<DicomSequence>())
        {
            foreach (var item in sequence.Items)
            {
                AssertNoPrivateElements(item);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ImagingResults.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
