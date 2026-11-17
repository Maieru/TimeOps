FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/TimeOps.Domain/TimeOps.Domain.csproj src/TimeOps.Domain/
COPY src/TimeOps.Application/TimeOps.Application.csproj src/TimeOps.Application/
COPY src/TimeOps.Infrastructure/TimeOps.Infrastructure.csproj src/TimeOps.Infrastructure/
COPY src/TimeOps.Web/TimeOps.Web.csproj src/TimeOps.Web/
RUN dotnet restore src/TimeOps.Web/TimeOps.Web.csproj --disable-parallel
COPY src/ src/
RUN dotnet publish src/TimeOps.Web/TimeOps.Web.csproj --no-restore -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN mkdir -p /data/keys && chown -R app:app /data
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DataProtection__KeyPath=/data/keys
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "TimeOps.Web.dll"]
