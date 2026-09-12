using DantesRoleplay.Interactions;

namespace DantesRoleplay.Media;

public sealed record ReadModelMediaOwnerAuthorization(
    string QueryContractFingerprint,
    string Role,
    string ResultPointer,
    string AvailabilityPointer,
    string AvailabilityValue);

// A link identifies a view to reauthorize, never a cached permission or a raw blob URL.
public sealed record ReadModelMediaTicket(ApplicationReadModelRequest Request, string CampaignId,
    string ObserverId, string OwnerId, string MediaId, string AttachmentFingerprint,
    ReadModelMediaOwnerAuthorization? OwnerAuthorization = null);

public interface IReadModelMediaLinkStore
{
    string GetOrCreate(ReadModelMediaTicket ticket);
    ReadModelMediaTicket? Find(string token);
}
