// Глобальные using тестового проекта. Тестовые файлы намеренно не перечисляют
// локальные using-директивы для доменных/инфраструктурных пространств имён —
// все зависимости перечислены здесь один раз.

global using Xunit;

global using GeoPulse.CHIngest;
global using GeoPulse.Consumer;
global using GeoPulse.Core.Events;
global using GeoPulse.Core.Models;
global using GeoPulse.Core.Repositories;
global using GeoPulse.Infrastructure;
global using GeoPulse.Infrastructure.Entities;
global using GeoPulse.Ingestion.Service;
global using GeoPulse.WebAPI;
global using GeoPulse.WebAPI.Controllers;
global using GeoPulse.WebAPI.Models;

global using GeoPulse.Tests.Integration;
global using GeoPulse.Tests.TestHelpers;

global using ClickHouse.Client.ADO;
global using ClosedXML.Excel;
global using Confluent.Kafka;
global using Confluent.Kafka.Admin;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.TestHost;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging.Abstractions;
global using Testcontainers.ClickHouse;
global using Testcontainers.Kafka;
global using Testcontainers.PostgreSql;
