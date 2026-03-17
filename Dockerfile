FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY MembershipPlatform.sln .
COPY src/MembershipService.Domain/MembershipService.Domain.csproj src/MembershipService.Domain/
COPY src/MembershipService.Application/MembershipService.Application.csproj src/MembershipService.Application/
COPY src/MembershipService.Infrastructure/MembershipService.Infrastructure.csproj src/MembershipService.Infrastructure/
COPY src/MembershipService.Api/MembershipService.Api.csproj src/MembershipService.Api/

RUN dotnet restore src/MembershipService.Api/MembershipService.Api.csproj

# Copy everything else and build
COPY src/ src/
RUN dotnet publish src/MembershipService.Api/MembershipService.Api.csproj \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MembershipService.Api.dll"]
