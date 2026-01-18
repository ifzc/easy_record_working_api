namespace EasyRecordWorkingApi.Contracts;

public class ApiResponse<T>
{
    public int Code { get; init; }
    public string Message { get; init; } = "ok";
    public T? Data { get; init; }
    public string? Details { get; init; }

    public static ApiResponse<T> Ok(T data) => new()
    {
        Code = 0,
        Message = "ok",
        Data = data
    };

    public static ApiResponse<T> Fail(int code, string message, string? details = null) => new()
    {
        Code = code,
        Message = message,
        Details = details
    };
}
