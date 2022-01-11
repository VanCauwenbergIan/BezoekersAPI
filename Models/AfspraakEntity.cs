using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace BezoekersAPI.Models
{
    public class AfspraakEntity : TableEntity
    {
        public AfspraakEntity()
        {

        }

        public AfspraakEntity(string datum, string afspraakId)
        {
            // note: je kan geen bepaalde karakters binnen een partitionkey gebruiken zoals '/'
            this.PartitionKey = datum;
            this.RowKey = afspraakId;
        }

        public string Voornaam { get; set; }
        public string Naam { get; set; }
        public string Email { get; set; }
        public string Telefoon { get; set; }
        public string Tijdstip { get; set; }
    }
}
