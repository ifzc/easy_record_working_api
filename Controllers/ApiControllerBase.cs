using EasyRecordWorkingApi.Contracts;
using EasyRecordWorkingApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyRecordWorkingApi.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    private readonly IUserContext _userContext;

    protected ApiControllerBase(IUserContext userContext)
    {
        _userContext = userContext;
    }

    protected Guid GetTenantId()
    {
        return _userContext.TenantId ?? Guid.Empty;
    }

    protected Guid GetUserId()
    {
        return _userContext.UserId ?? Guid.Empty;
    }

    protected IActionResult Success<T>(T data)
    {
        return Ok(ApiResponse<T>.Ok(data));
    }

    protected IActionResult Failure(int httpStatus, int code, string message, string? details = null)
    {
        return StatusCode(httpStatus, ApiResponse<object>.Fail(code, message, details));
    }
}
