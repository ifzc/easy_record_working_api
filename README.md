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

## Docker

Build:

```
docker build -t easy_record_working_api:latest .
```

Run (set your own values):

```
docker run -p 8080:8080 ^
  -e ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=easy_record_working;User ID=root;Password=your_password;" ^
  -e Jwt__Issuer="EasyRecordWorkingApi" ^
  -e Jwt__Audience="EasyRecordWorkingApi" ^
  -e Jwt__Key="ReplaceWithAStrongKeyAtLeast32Chars" ^
  -e Jwt__ExpiresMinutes=720 ^
  easy_record_working_api:latest
```
