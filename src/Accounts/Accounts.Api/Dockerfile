# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY BankingDomain.sln ./
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Accounts/Accounts.Domain/Accounts.Domain.csproj src/Accounts/Accounts.Domain/
COPY src/Accounts/Accounts.Application/Accounts.Application.csproj src/Accounts/Accounts.Application/
COPY src/Accounts/Accounts.Infrastructure/Accounts.Infrastructure.csproj src/Accounts/Accounts.Infrastructure/
COPY src/Accounts/Accounts.Api/Accounts.Api.csproj src/Accounts/Accounts.Api/
RUN dotnet restore src/Accounts/Accounts.Api/Accounts.Api.csproj
COPY . .
RUN dotnet publish src/Accounts/Accounts.Api/Accounts.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# REST trên 8080 (Http1), gRPC trên 8090 (Http2 h2c) — cấu hình qua Kestrel:Endpoints (appsettings)
EXPOSE 8080
EXPOSE 8090
ENTRYPOINT ["dotnet", "Accounts.Api.dll"]
