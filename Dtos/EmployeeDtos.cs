namespace EasyRecordWorkingApi.Dtos;

public class EmployeeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateEmployeeRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class UpdateEmployeeRequest
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool? IsActive { get; set; }
    public string? Remark { get; set; }
}

public class ImportEmployeesResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
}

public class ImportEmployeesRequest
{
    public IFormFile File { get; set; } = default!;
}
