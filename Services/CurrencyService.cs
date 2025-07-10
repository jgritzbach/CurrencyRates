using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace .Services
{

    public class CurrencyService : Interfaces.ICurrencyService
    {
        private readonly Db db;
        private readonly IConfiguration configuration;
        private readonly ILogger<CurrencyService> logger;
        private readonly string cNBSiteUrl;

        public CurrencyService(Db db, IConfiguration configuration, ILogger<CurrencyService> logger)
        {
            this.db = db;
            this.configuration = configuration;
            this.logger = logger;
            cNBSiteUrl = configuration.GetRequired<string>(Constants.C.Settings.CNBSiteUrl);
        }

        /// <summary>
        /// Updates all currencies in the db to have fresh exchange rate to CZK. 
        /// Uses CNB API for this task. 
        /// </summary>
        public async Task UpdateAllExchangeRates()
        {
            // fetch fresh data from CNB
            string CSVdata = await FetchCSVFromCNBApi();

            var currencies = await db.Currencies.ToListAsync();

            foreach (var currency in currencies)
            {
                try
                {
                    await UpdateExchangeRateToCZK(currency, CSVdata);
                }
                catch (Exception ex)
                {
                    logger.LogError($"Updating exchange rate of currency: {currency.Title} failed");
                }
            }
        }

        /// <summary>
        /// For a given currency, update it's exchange rate to CZK based on data provided by CNB API. 
        /// </summary>
        /// <remarks>
        /// This method accepts string, presuming it is a fetched CSV file retrieved from CNB API (to save time in multiple calls). 
        /// If such CSV string is not provided, it is retrieved on the go by the CNB API itself
        /// </remarks>
        /// <param name="currency">Which currency entity should have it's exchange rate updated</param>
        /// <param name="cnbCSV">The CSV file from CNB API - if omited, method fetches it itself</param>
        public async Task UpdateExchangeRateToCZK(Currency currency, string? cnbCSV = null)
        {
            // No need to update CZK, it is always 1
            if (currency.Short == "CZK" || currency.Symbol == "Kč")
            {
                return;
            }

            decimal exchangeRateToCZK = await PickExchangeRateFromCnbCSV(currency, cnbCSV); // CSV can be null, it will be fetched later if needed

            decimal? previousExchangeRate = currency.ExchangeRateToCZK; // remember previous value for logging
            currency.ExchangeRateToCZK = exchangeRateToCZK;
            await db.SaveChangesAsync();
            logger.LogInformation($"Exchange rate of currency: {currency.Title} updated from {previousExchangeRate} to {currency.ExchangeRateToCZK}");
        }

        /// <summary>
        /// Retrieves data about exchange rates as simple CSV file from CNB API. 
        /// <remarks>
        /// You can provide specific date to recieve data from that date. If not provided, today's data are retrieved.
        /// </remarks>
        /// </summary>
        /// <returns>CSV string</returns>
        public async Task<string> FetchCSVFromCNBApi(DateTime? date = null)
        {

            try
            {
                // use Http request to obtain data from CNB API
                using HttpClient client = new HttpClient();
                {
                    // building the url
                    string dateInUrl = date == null ? "" : date.Value.ToString("dd.MM.yyyy");
                    string url = cNBSiteUrl + dateInUrl;

                    // request itself
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    HttpResponseMessage response = await client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogError($"CNB API returned an error status code: {response.StatusCode} for URL: {url}");
                        throw new HttpRequestException($"CNB API error: {response.StatusCode}");
                    }

                    string responseText = await response.Content.ReadAsStringAsync();
                    return responseText;    // should return CSV string
                }
            } 
            catch (Exception ex)
            { 
                logger.LogError(ex, $"Error while fetching exchange rates from CNB API for URL {cNBSiteUrl}");
                throw;
            }

        }

        /// <summary>
        /// For a given currency, retrieves it's exchange rate to czech crowns as decimal. 
        /// </summary>
        /// <remarks>
        /// If you already have the CSV data from CNB API, you can provide it to the method for even faster result. 
        /// Otherwise, the CSV will be obtained in the process. 
        /// </remarks>
        /// <param name="currency">To which currency do you need the exchange rate to CZK</param>
        /// <param name="cnbCSV">Raw CSV file from CNB Api - optional, methods fetches it if necessary</param>
        /// <param name="date">Optional - you can specify for which date you are interested. Today is assumed if omited</param>
        /// <returns></returns>
        public async Task<decimal> PickExchangeRateFromCnbCSV(Currency currency, string? cnbCSV = null, DateTime? date = null)
        {
            // Asking for CZK exchange rate always returns 1, because exchange rate is always compared to basic currency, which is CZK
            if(currency.Short == "CZK" || currency.Symbol == "Kč")
            {
                return (decimal) 1;
            }

            // If data from CNB were not provided, let's obtain them now
            if(cnbCSV == null)
            {
                cnbCSV = await FetchCSVFromCNBApi(date); // date is optional
            }
            
            // Build the exchange rate data into an object
            CnbExchangeRate currencyData = GetExchangeRateFromRawCSV(currency, cnbCSV);

            decimal exchangeRate = currencyData.Rate / currencyData.Amount; // Some currencies have very little value to CZK => CNB represents them as a hundredfold - you need to divide Rate by Amount to get true exchange rate

            return exchangeRate;
        }

        /// <summary>
        /// Finds the line of a given currency in CSV file provided by the CNB API.
        /// Uses the line to model a CnbExchangeRate object and returns it.  
        /// </summary>
        /// <param name="currency"></param>
        /// <param name="rawCSV"></param>
        /// <returns>CnbExchangeRate object of a corresponding currency filled with data</returns>
        private CnbExchangeRate GetExchangeRateFromRawCSV(Currency currency, string rawCSV)
        {
            string[] lines = rawCSV.Split('\n');
            var dataLines = lines.Skip(2); // skips the date and the header

            // from each CSV line, let's construct an object to hold data
            foreach (var line in dataLines)
            {

                CnbExchangeRate exchangeRateDTO = BuildExchangeRateDTOFromCSVLine(line);
                
                // If the iterated line is the one we are looking for
                if (exchangeRateDTO.CurrencyTitle == currency.Title || exchangeRateDTO.CurrencyCode == currency.Short)
                {
                    return exchangeRateDTO;    // just return it
                }

            }
            
            return new CnbExchangeRate();   // if no CSV line matched the given currency, return empty object

        }

        /// <summary>
        /// From a single line of CNB CSV data builds the exchange rate model object
        /// </summary>
        /// <returns>A CnbExchangeRate object modeled from CSV data</returns>
        private CnbExchangeRate BuildExchangeRateDTOFromCSVLine(string line)
        {
            // The CNB API always contains 5 columns of data. Let's parse them into an object
            var columns = line.Split('|');

            CnbExchangeRate exchangeRateDTO = new CnbExchangeRate();
            exchangeRateDTO.Country = columns[0].Trim();
            exchangeRateDTO.CurrencyTitle = columns[1].Trim();
            exchangeRateDTO.Amount = decimal.Parse(columns[2], CultureInfo.InvariantCulture);  // Because some currencies have very little value to CZK, CNP represents them as a hundredfold - you need to divide Rate by Amount to get true exchange rate
            exchangeRateDTO.CurrencyCode = columns[3].Trim();
            exchangeRateDTO.Rate = decimal.Parse(columns[4].Replace(",", "."), CultureInfo.InvariantCulture);

            return exchangeRateDTO;
        }


    }
}
