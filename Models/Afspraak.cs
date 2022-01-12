using System;
using System.Collections.Generic;
using System.Text;

namespace BezoekersAPI.Models
{
    public class Afspraak
    {
        public string AfspraakId { get; set; }
        public string Datum { get; set; }
        public string Voornaam { get; set; }
        public string Naam { get; set; }
        public string Email { get; set; }
        public string Telefoon { get; set; }
        public string Tijdstip { get; set; }
        public string Locatie { get; set; }
    }
}
