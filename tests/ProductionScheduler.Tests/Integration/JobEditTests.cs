using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// The edit form sends the job's whole editable state: blank boxes clear their value, and a blank
// product keeps the current one.
[Collection(IntegrationCollection.Name)]
public class JobEditTests
{
    private readonly IntegrationFixture _f;

    public JobEditTests(IntegrationFixture fixture) => _f = fixture;

    [Fact]
    public async Task ClearingFields_ClearsThemOnTheJob_ButNeverTheProduct()
    {
        await _f.AddStationAsync("Edit Line", "EditProduct");
        var id = await _f.WithDbAsync(async db =>
        {
            var job = new Job
            {
                JobRef = "EDIT-1", ComponentType = "EditProduct", DesignStatus = "FinalDesign", Status = "ready", TotalEqA = 10,
                ClientName = "Old client", ClientRefNumber = "CR-9", InvoiceNumber = "INV-1",
                BookedDeliveryDate = DateOnly.FromDateTime(SiteClock.Now).AddDays(9),
                EstBookedDeliveryDate = DateOnly.FromDateTime(SiteClock.Now).AddDays(8),
                CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
            };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            return job.Id;
        });
        var admin = await _f.ClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/jobs/{id}", new
        {
            componentType = (string?)null,
            clientName = "  New client  ",
            clientRefNumber = "",
            invoiceNumber = "",
            bookedDeliveryDate = (string?)null,
            estBookedDeliveryDate = DateOnly.FromDateTime(SiteClock.Now).AddDays(12),
            delivered = false,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("job");
        Assert.Equal("EditProduct", job.GetProperty("componentType").GetString());
        Assert.Equal("New client", job.GetProperty("clientName").GetString());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("clientRefNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, job.GetProperty("invoiceNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, job.GetProperty("bookedDeliveryDate").ValueKind);
        Assert.Equal(DateOnly.FromDateTime(SiteClock.Now).AddDays(12),
            await _f.WithDbAsync(db => db.Jobs.Where(j => j.Id == id).Select(j => j.EstBookedDeliveryDate).SingleAsync()));
    }
}
