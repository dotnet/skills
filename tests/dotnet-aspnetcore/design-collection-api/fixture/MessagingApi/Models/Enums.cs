namespace MessagingApi.Models;

public enum NamespaceSku { Basic, Standard, Premium }

public enum ProvisioningState { Creating, Updating, Succeeded, Deleting, Failed }

public enum EntityStatus { Active, Disabled }

[Flags]
public enum AccessRight { None = 0, Listen = 1, Send = 2, Manage = 4 }
