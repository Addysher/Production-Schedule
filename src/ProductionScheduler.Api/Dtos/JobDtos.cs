using ProductionScheduler.Scheduling;

namespace ProductionScheduler.Api.Dtos;

public record JobSummaryDto(
    string JobRef, string? ComponentType, string? ClientName, int? StationId, string? StationName,
    DateOnly? DueDate, decimal? TotalEqA, string Status, bool IsLate);

public record JobDetailDto(
    string JobRef, string? ComponentType, string? ClientName, string? SiteAddress, string? SiteSuburb,
    string? SupervisorName, string? SupervisorNumber, DateOnly? JobOrderDate, TimeOnly? JobOrderTime,
    decimal? TotalEqA, DateTime? EstimatedStartDate, DateTime? EstimatedCompletionDate,
    DateOnly? ExpectedDeliveryDate, DateOnly? EstBookedDeliveryDate, DateOnly? BookedDeliveryDate,
    string? InvoiceNumber, bool Delivered, string Status, bool IsLate, string DesignStatus,
    int? AssignedStationId, bool IsManuallyScheduled, int? ManualStationId, DateOnly? ManualScheduleDate);

public record CreateJobRequest(
    string JobRef, string ComponentType, string? ClientName, DateOnly? ExpectedDeliveryDate,
    DateOnly? EstBookedDeliveryDate, string? ClientRefNumber, string? SiteAddress, string? SiteSuburb,
    string? SupervisorName, string? SupervisorNumber, string DesignStatus);

public record UpdateJobRequest(
    string? ComponentType, string? ClientName, DateOnly? ExpectedDeliveryDate, DateOnly? EstBookedDeliveryDate,
    string? ClientRefNumber, string? SiteAddress, string? SiteSuburb, string? SupervisorName, string? SupervisorNumber,
    DateOnly? BookedDeliveryDate, string? InvoiceNumber, bool Delivered);

public record ScheduleManuallyRequest(int StationId, DateOnly ScheduleDate);

public record UpdateJobResponse(JobDetailDto Job, ScheduleGenerationResult Schedule);
