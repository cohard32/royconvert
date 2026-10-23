namespace RoyConvert.Core;

public interface IConverter
{
    /// <param name="progress">Receives values in [0, 1]; null means progress is indeterminate.</param>
    Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken);
}

public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
