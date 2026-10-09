FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Sehatak.Domain/Sehatak.Domain.csproj Sehatak.Domain/
COPY Sehatak.Application/Sehatak.Application.csproj Sehatak.Application/
COPY Sehatak.Infrastructure/Sehatak.Infrastructure.csproj Sehatak.Infrastructure/
COPY Sehatak.API/Sehatak.API.csproj Sehatak.API/

RUN dotnet restore Sehatak.API/Sehatak.API.csproj

COPY . .
RUN dotnet publish Sehatak.API/Sehatak.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "Sehatak.API.dll"]
