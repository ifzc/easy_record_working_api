namespace EasyRecordWorkingApi.Dtos;

public class EmployeeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? WorkType { get; set; }
    public string? Phone { get; set; }
    public string? IdCardNumber { get; set; }
    public string? Remark { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateEmployeeRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? WorkType { get; set; }
    public string? Phone { get; set; }
    public string? IdCardNumber { get; set; }
    public string? Remark { get; set; }
    public List<string>? Tags { get; set; }
}

public class UpdateEmployeeRequest
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? WorkType { get; set; }
    public string? Phone { get; set; }
    public string? IdCardNumber { get; set; }
    public string? Remark { get; set; }
    public List<string>? Tags { get; set; }
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
