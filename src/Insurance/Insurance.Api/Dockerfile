# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY BankingDomain.sln ./
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Insurance/Insurance.Domain/Insurance.Domain.csproj src/Insurance/Insurance.Domain/
COPY src/Insurance/Insurance.Application/Insurance.Application.csproj src/Insurance/Insurance.Application/
COPY src/Insurance/Insurance.Infrastructure/Insurance.Infrastructure.csproj src/Insurance/Insurance.Infrastructure/
COPY src/Insurance/Insurance.Api/Insurance.Api.csproj src/Insurance/Insurance.Api/
RUN dotnet restore src/Insurance/Insurance.Api/Insurance.Api.csproj
COPY . .
RUN dotnet publish src/Insurance/Insurance.Api/Insurance.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Insurance.Api.dll"]
