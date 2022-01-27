using BezoekersAPI.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BezoekersAPI
{
    public class SendMail
    {
        [FunctionName("SendMail")]
        public async Task Run([QueueTrigger("afsprakenmails", Connection = "ConnectionStringStorage")]string myQueueItem, ILogger log)
        {
            Afspraak afspraak = JsonConvert.DeserializeObject<Afspraak>(myQueueItem);

            await Send(afspraak);
        }
        private async Task Send(Afspraak afspraak)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("SendGridAPIKey");
                var client = new SendGridClient(apiKey);
                var from = new EmailAddress("ian.van.cauwenberg@student.howest.be");
                var subject = $"Registratie ontvangen";
                var to = new EmailAddress(afspraak.Email);
                var body = $"Beste {afspraak.Voornaam}, \n\nWe hebben je afspraak voor {afspraak.Datum} om {afspraak.Tijdstip}u goed ontvangen. \n\nDe volgende link bevat uw persoonlijke qr-code: http://temi-mobile.azurewebsites.net/qr-code.html?qrcode={afspraak.AfspraakId} \n\nTemi zal uw ontvangen aan het onthaal.";
                var msg = MailHelper.CreateSingleEmail(from, to, subject, body, "");
                await client.SendEmailAsync(msg);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
