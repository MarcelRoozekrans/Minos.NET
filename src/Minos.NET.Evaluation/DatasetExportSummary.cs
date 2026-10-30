using System.Globalization;
using System.Text;

namespace Minos.Evaluation;

/// <summary>
/// What an export of recorded decisions did: the decisions it read, the items and signals it wrote, and the decisions
/// and confirmed keys it left out, per reason. It holds counts only, never a decision id, an erasure key or any state.
/// </summary>
public sealed class DatasetExportSummary
{
    internal DatasetExportSummary(
        int decisionsRead,
        int itemsWritten,
        int signalsWritten,
        int signalsSkipped,
        int skippedWithoutConfirmedOutcome,
        int skippedWithImages,
        int skippedWithoutValidKey,
        int skippedInvalidItem,
        int keysDropped,
        int keysOverridden)
    {
        DecisionsRead = decisionsRead;
        ItemsWritten = itemsWritten;
        SignalsWritten = signalsWritten;
        SignalsSkipped = signalsSkipped;
        SkippedWithoutConfirmedOutcome = skippedWithoutConfirmedOutcome;
        SkippedWithImages = skippedWithImages;
        SkippedWithoutValidKey = skippedWithoutValidKey;
        SkippedInvalidItem = skippedInvalidItem;
        KeysDropped = keysDropped;
        KeysOverridden = keysOverridden;
    }

    /// <summary>Gets the number of decisions read.</summary>
    public int DecisionsRead { get; }

    /// <summary>Gets the number of dataset items written, one per exported decision.</summary>
    public int ItemsWritten { get; }

    /// <summary>Gets the number of signal outcomes of exported items, written to the signals stream when there is one.</summary>
    public int SignalsWritten { get; }

    /// <summary>Gets the number of signal outcomes left out because their decision was skipped.</summary>
    public int SignalsSkipped { get; }

    /// <summary>Gets the number of decisions skipped because no outcome confirmed an answer.</summary>
    public int SkippedWithoutConfirmedOutcome { get; }

    /// <summary>Gets the number of decisions skipped because they sent images, which a record keeps only as hashes.</summary>
    public int SkippedWithImages { get; }

    /// <summary>Gets the number of decisions skipped because none of their confirmed answers fit a question.</summary>
    public int SkippedWithoutValidKey { get; }

    /// <summary>Gets the number of decisions skipped because their item would not load as a dataset line, or because an earlier item has their id.</summary>
    public int SkippedInvalidItem { get; }

    /// <summary>
    /// Gets the number of confirmed answers dropped because they did not fit their question: an unknown key, another
    /// kind, an unknown option or a level out of range.
    /// </summary>
    public int KeysDropped { get; }

    /// <summary>
    /// Gets the number of question keys whose winning answer differs from an earlier valid answer to the same key,
    /// counted once per item.
    /// </summary>
    public int KeysOverridden { get; }

    /// <summary>Gets one line per count that is not zero, each a fixed label and the count, for example <c>items written: 3</c>.</summary>
    /// <returns>The text, with the lines separated by <c>\n</c>; empty when every count is zero.</returns>
    public override string ToString()
    {
        var text = new StringBuilder();
        Append(text, "decisions read", DecisionsRead);
        Append(text, "items written", ItemsWritten);
        Append(text, "signals written", SignalsWritten);
        Append(text, "signals skipped with their decision", SignalsSkipped);
        Append(text, "skipped without a confirmed outcome", SkippedWithoutConfirmedOutcome);
        Append(text, "skipped with images", SkippedWithImages);
        Append(text, "skipped without a valid confirmed key", SkippedWithoutValidKey);
        Append(text, "skipped as an item the dataset loader rejects", SkippedInvalidItem);
        Append(text, "confirmed keys dropped", KeysDropped);
        Append(text, "confirmed keys overridden", KeysOverridden);
        return text.ToString();
    }

    private static void Append(StringBuilder text, string label, int count)
    {
        if (count == 0)
        {
            return;
        }

        if (text.Length > 0)
        {
            text.Append('\n');
        }

        text.Append(label).Append(": ").Append(count.ToString(CultureInfo.InvariantCulture));
    }
}
