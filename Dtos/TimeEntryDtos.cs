namespace EasyRecordWorkingApi.Dtos;

public class TimeEntryDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeType { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public decimal NormalHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalHours { get; set; }
    public decimal WorkUnits { get; set; }
}

public class CreateTimeEntryRequest
{
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal NormalHours { get; set; } = 8;
    public decimal OvertimeHours { get; set; }
    public string? Remark { get; set; }
}

public class UpdateTimeEntryRequest
{
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal NormalHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public string? Remark { get; set; }
}

public class TimeEntrySummaryDto
{
    public DateOnly Date { get; set; }
    public decimal NormalHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal TotalHours { get; set; }
    public decimal TotalWorkUnits { get; set; }
    public int Headcount { get; set; }
}

public class BatchCreateTimeEntriesRequest
{
    public List<Guid> EmployeeIds { get; set; } = new();
    public List<DateOnly> WorkDates { get; set; } = new();
    public decimal NormalHours { get; set; } = 8;
    public decimal OvertimeHours { get; set; }
    public string? Remark { get; set; }
}

public class BatchCreateTimeEntriesResult
{
    public int Total { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public List<BatchCreateTimeEntryDetail> Details { get; set; } = new();
}

public class BatchCreateTimeEntryDetail
{
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
