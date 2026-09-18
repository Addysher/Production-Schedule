namespace ProductionScheduler.Api.Dtos;

public record QuoteLineRequest(int JobTypeId, int Qty);

public record QuoteEstimateRequest(string Ref, List<QuoteLineRequest> Lines);

public record QuoteLineResult(int JobTypeId, string JobTypeName, int Qty, DateTime EstimatedCompletion);

public record QuoteEstimateResponse(string Ref, List<QuoteLineResult> Lines, DateTime FullOrderReadyAt);
