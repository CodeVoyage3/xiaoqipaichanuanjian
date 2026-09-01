using StoreExpiryInspector.Application.Tasks;

namespace StoreExpiryInspector.UI;

public sealed class InspectionHistoryDetailRowViewModel
{
    public InspectionHistoryDetailRowViewModel(InspectionHistoryItemDetail item, int displayBatchNumber)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (displayBatchNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayBatchNumber));
        }

        Item = item;
        DisplayBatchNumber = displayBatchNumber;
    }

    public InspectionHistoryItemDetail Item { get; }

    public int DisplayBatchNumber { get; }
}
