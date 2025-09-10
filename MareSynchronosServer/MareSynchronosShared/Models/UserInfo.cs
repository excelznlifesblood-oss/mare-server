namespace MareSynchronosShared.Models;

public record UserInfo(string Alias, bool IndividuallyPaired, bool IsSynced, List<string> GIDs, UserPermissionSet? OwnPermissions, UserPermissionSet? OtherPermissions);