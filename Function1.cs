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
                            Tijdstip = result.Tijdstip
                        };

                        afspraken.Add(afspraak);
                    }

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
                        Tijdstip = afspraak.Tijdstip
                    };

                    TableOperation insertOperation = TableOperation.Insert(afspraakEntity);

                    await table.ExecuteAsync(insertOperation);

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
                        Tijdstip = afspraak.Tijdstip
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
    }
}
