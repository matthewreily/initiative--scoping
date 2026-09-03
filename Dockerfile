FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY InitiativeScoping.sln ./
COPY src/InitiativeScoping.Domain/InitiativeScoping.Domain.csproj src/InitiativeScoping.Domain/
COPY src/InitiativeScoping.Application/InitiativeScoping.Application.csproj src/InitiativeScoping.Application/
COPY src/InitiativeScoping.Infrastructure/InitiativeScoping.Infrastructure.csproj src/InitiativeScoping.Infrastructure/
COPY src/InitiativeScoping.Web/InitiativeScoping.Web.csproj src/InitiativeScoping.Web/
RUN dotnet restore src/InitiativeScoping.Web/InitiativeScoping.Web.csproj
COPY src/ src/
RUN dotnet publish src/InitiativeScoping.Web/InitiativeScoping.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ForwardedHeaders__Enabled=true \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "InitiativeScoping.Web.dll"]
