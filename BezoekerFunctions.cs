using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Cosmos.Table;
using System.Collections.Generic;
using BezoekersAPI.Models;
using Azure.Storage.Queues;
using System.Text;
using System.Linq;

namespace BezoekersAPI
{
    public class BezoekerFunctions
    {
        [FunctionName("Afspraken")]
        public async Task<IActionResult> Afspraken(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "afspraken")] HttpRequest req,
            ILogger log)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("ConnectionStringStorage");

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("bezoekers");

                // GET (alle afspraken)
                if (req.Method == HttpMethods.Get)
                {
                    TableContinuationToken token = null;

                    var results = new List<AfspraakEntity>();

                    // haalt volledige table op zonder voorwaarden of bereikbare limiet
                    do
                    {
                        var queryResult = table.ExecuteQuerySegmented(new TableQuery<AfspraakEntity>(), token);
                        results.AddRange(queryResult.Results);
                        token = queryResult.ContinuationToken;
                    }
                    while (token != null);

                    // probleem: de datum en guid worden met de keys 'partitionKey' en 'rowKey' gereturned in de json + [JsonProperty("propertyKey")] is niet geldig in hun context => we recycelen de afspraak klasse 
                    var afspraken = new List<Afspraak>();

                    foreach (var result in results)
                    {
                        Afspraak afspraak = new Afspraak()
                        {
                            AfspraakId = result.RowKey,
                            Datum = result.PartitionKey,
                            Voornaam = result.Voornaam,
                            Naam = result.Naam,
                            Email = result.Email,
                            Telefoon = result.Telefoon,
                            Tijdstip = result.Tijdstip,
                            Locatie = result.Locatie
                        };

                        afspraken.Add(afspraak);
                    }

                    afspraken = SortAfspraken(afspraken);

                    return new OkObjectResult(afspraken);
                }
                // POST
                else
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    var afspraak = JsonConvert.DeserializeObject<Afspraak>(requestBody);

                    string afspraakId = Guid.NewGuid().ToString();
                    afspraak.AfspraakId = afspraakId;

                    AfspraakEntity afspraakEntity = new AfspraakEntity(afspraak.Datum, afspraakId)
                    {
                        Voornaam = afspraak.Voornaam,
                        Naam = afspraak.Naam,
                        Email = afspraak.Email,
                        Telefoon = afspraak.Telefoon,
                        Tijdstip = afspraak.Tijdstip,
                        Locatie = afspraak.Locatie
                    };

                    TableOperation insertOperation = TableOperation.Insert(afspraakEntity);

                    await table.ExecuteAsync(insertOperation);

                    await SendToQueue(afspraak);

                    return new OkObjectResult(afspraak);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());

                return new StatusCodeResult(500);
            }
        }

        [FunctionName("Afspraak")]
        public async Task<IActionResult> Afspraak(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "put", "delete", Route = "afspraken/{id}")] HttpRequest req, string id,
            ILogger log)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("ConnectionStringStorage");

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("bezoekers");
                
                // GET (1 afspraak)
                if (req.Method == HttpMethods.Get)
                {
                    TableQuery<AfspraakEntity> query = new TableQuery<AfspraakEntity>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, id));

                    var result = await table.ExecuteQuerySegmentedAsync<AfspraakEntity>(query, null);

                    Afspraak afspraak = new Afspraak();

                    foreach (AfspraakEntity afspraakEntity in result.Results)
                    {
                        afspraak.AfspraakId = id;
                        afspraak.Datum = afspraakEntity.PartitionKey;
                        afspraak.Naam = afspraakEntity.Naam;
                        afspraak.Voornaam = afspraakEntity.Voornaam;
                        afspraak.Email = afspraakEntity.Email;
                        afspraak.Telefoon = afspraakEntity.Telefoon;
                        afspraak.Tijdstip = afspraakEntity.Tijdstip;
                        afspraak.Locatie = afspraakEntity.Locatie;
                    }

                    return new OkObjectResult(afspraak);
                }
                // PUT
                else if (req.Method == HttpMethods.Put)
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    var afspraak = JsonConvert.DeserializeObject<Afspraak>(requestBody);

                    AfspraakEntity afspraakEntity = new AfspraakEntity(afspraak.Datum, id)
                    {
                        Voornaam = afspraak.Voornaam,
                        Naam = afspraak.Naam,
                        Email = afspraak.Email,
                        Telefoon = afspraak.Telefoon,
                        Tijdstip = afspraak.Tijdstip,
                        Locatie = afspraak.Locatie
                    };

                    // Replace heeft een ETag nodig en partitonKey mag niet veranderen
                    // InsertOrReplace is hiervoor geen mogelijkheid (bvb. 2 afspraken met ander datum op 1 naam zou wel kunnen, maar hiervoor zou je POST gebruiken)
                    TableQuery<AfspraakEntity> query = new TableQuery<AfspraakEntity>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, id));

                    var result = await table.ExecuteQuerySegmentedAsync<AfspraakEntity>(query, null);

                    afspraakEntity.ETag = result.Results[0].ETag;

                    if (afspraakEntity.PartitionKey != result.Results[0].PartitionKey)
                    {
                        var deleteOperation = TableOperation.Delete(result.Results[0]);
                        await table.ExecuteAsync(deleteOperation);

                        TableOperation insertOperation = TableOperation.Insert(afspraakEntity);
                        await table.ExecuteAsync(insertOperation);
                    }
                    else
                    {
                        TableOperation replaceOperation = TableOperation.Replace(afspraakEntity);
                        await table.ExecuteAsync(replaceOperation);
                    }

                    return new OkObjectResult(afspraakEntity);
                }
                // DELETE
                else
                {
                    TableQuery<AfspraakEntity> query = new TableQuery<AfspraakEntity>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, id));

                    var result = await table.ExecuteQuerySegmentedAsync<AfspraakEntity>(query, null);

                    foreach(AfspraakEntity afspraakEntity in result.Results)
                    {
                        var deleteOperation = TableOperation.Delete(afspraakEntity);

                        await table.ExecuteAsync(deleteOperation);
                    }

                    return new OkObjectResult($"Afspraak verwijderd met id: {id}");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());

                return new StatusCodeResult(500);
            }
        }

        [FunctionName("AfsprakenVoorMail")]
        public async Task<IActionResult> AfsprakenVoorMail(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "afsprakenvoormail/{mail}")] HttpRequest req, string mail,
            ILogger log)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("ConnectionStringStorage");

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("bezoekers");

                TableQuery<AfspraakEntity> query = new TableQuery<AfspraakEntity>().Where(TableQuery.GenerateFilterCondition("Email", QueryComparisons.Equal, mail));

                TableContinuationToken token = null;

                var results = new List<AfspraakEntity>();

                do
                {
                    var queryResult = table.ExecuteQuerySegmented(query, token);
                    results.AddRange(queryResult.Results);
                    token = queryResult.ContinuationToken;
                }
                while (token != null);

                var afspraken = new List<Afspraak>();

                foreach (var result in results)
                {
                    Afspraak afspraak = new Afspraak()
                    {
                        AfspraakId = result.RowKey,
                        Datum = result.PartitionKey,
                        Voornaam = result.Voornaam,
                        Naam = result.Naam,
                        Email = result.Email,
                        Telefoon = result.Telefoon,
                        Tijdstip = result.Tijdstip,
                        Locatie = result.Locatie
                    };

                    afspraken.Add(afspraak);
                }

                return new OkObjectResult(afspraken);
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());

                return new StatusCodeResult(500);
            }
        }

        [FunctionName("Locatie")]
        public async Task<IActionResult> Locatie(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "afspraken/{id}/locatie")] HttpRequest req, string id,
            ILogger log)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("ConnectionStringStorage");

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("bezoekers");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var data = JsonConvert.DeserializeObject<EnkelLocatie>(requestBody);

                TableQuery<AfspraakEntity> selectQuery = new TableQuery<AfspraakEntity>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, id));

                var resultSelect = await table.ExecuteQuerySegmentedAsync<AfspraakEntity>(selectQuery, null);

                Afspraak afspraak = new Afspraak();

                foreach (AfspraakEntity afspraakEntity in resultSelect.Results)
                {
                    afspraak.AfspraakId = id;
                    afspraak.Datum = afspraakEntity.PartitionKey;
                    afspraak.Naam = afspraakEntity.Naam;
                    afspraak.Voornaam = afspraakEntity.Voornaam;
                    afspraak.Email = afspraakEntity.Email;
                    afspraak.Telefoon = afspraakEntity.Telefoon;
                    afspraak.Tijdstip = afspraakEntity.Tijdstip;
                    afspraak.Locatie = afspraakEntity.Locatie;
                }

                afspraak.Locatie = data.Locatie;

                AfspraakEntity afspraakEntityUpdate = new AfspraakEntity(afspraak.Datum, id)
                {
                    Voornaam = afspraak.Voornaam,
                    Naam = afspraak.Naam,
                    Email = afspraak.Email,
                    Telefoon = afspraak.Telefoon,
                    Tijdstip = afspraak.Tijdstip,
                    Locatie = afspraak.Locatie
                };

                afspraakEntityUpdate.ETag = "*";

                TableOperation replaceOperation = TableOperation.Replace(afspraakEntityUpdate);
                await table.ExecuteAsync(replaceOperation);

                return new OkObjectResult(afspraakEntityUpdate);
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());

                return new StatusCodeResult(500);
            }
        }
        private async Task SendToQueue(Afspraak afspraak)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("ConnectionStringStorage");
                QueueClient queueClient = new QueueClient(connectionString, "afsprakenmails");

                await queueClient.CreateIfNotExistsAsync();

                string json = JsonConvert.SerializeObject(afspraak);
                var bytes = Encoding.UTF8.GetBytes(json);

                await queueClient.SendMessageAsync(Convert.ToBase64String(bytes));
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private List<Afspraak> SortAfspraken(List<Afspraak> afspraken)
        {
            afspraken = afspraken.OrderBy(afspraak => afspraak.Datum.Split('-')[2]).ThenBy(afspraak => afspraak.Datum.Split('-')[1]).ThenBy(afspraak => afspraak.Datum.Split('-')[0]).ThenBy(afspraak => afspraak.Tijdstip).ToList();

            return afspraken;
        }
    }
}
