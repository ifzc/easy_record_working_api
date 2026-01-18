# EasyRecordWorkingApi

## Quick Start

1. Copy the example config:

```
cp appsettings.Development.json.example appsettings.Development.json
```

2. Update `ConnectionStrings:Default` and `Jwt:Key` in `appsettings.Development.json`.

3. Initialize database with the provided SQL in `INITDB.md`.

4. Run the API:

```
dotnet run
```

## Notes

- `appsettings.Development.json` is ignored by git for safety.
- Swagger UI is available at `/swagger` in Development.
