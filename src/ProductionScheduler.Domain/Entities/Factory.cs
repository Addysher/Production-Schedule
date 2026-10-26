namespace ProductionScheduler.Domain.Entities;

// v3.0 — a building (or area) with its own stations, used to group the kiosk display. Jobs still go to
// any station that makes their product, whichever factory it's in.
public class Factory
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public ICollection<Station> Stations { get; set; } = new List<Station>();
}
