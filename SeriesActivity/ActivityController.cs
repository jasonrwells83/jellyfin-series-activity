using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SeriesActivity;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("SeriesActivity")]
public sealed class ActivityController(ActivityService service) : ControllerBase
{
    [HttpGet("Report")]
    public ActionResult<Report> GetReport([FromQuery] int inactiveDays = 90, [FromQuery] int graceDays = 14)
    {
        if (inactiveDays is < 1 or > 3650 || graceDays is < 0 or > 3650)
            return BadRequest("Inactivity must be 1–3650 days; observation must be 0–3650 days.");
        return service.GetReport(inactiveDays, graceDays);
    }

    [HttpPost("Refresh")]
    public ActionResult Refresh() { service.RequestRefresh(); return Accepted(); }

    [HttpPost("Keep/{id:guid}")]
    public ActionResult Keep(Guid id, [FromBody] KeepRequest request) =>
        service.SetKeep(id, request.Keep) ? NoContent() : NotFound();
}

public sealed record KeepRequest(bool Keep);
