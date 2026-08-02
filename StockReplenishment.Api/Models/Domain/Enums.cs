namespace StockReplenishment.Api.Models.Domain;

public enum RequestStatus { Draft, Submitted, Approved, Rejected, Fulfilled }
public enum RequestPriority { Low, Normal, Urgent }
public enum ValidationStatus { NotStarted, InProgress, Completed, Failed }
