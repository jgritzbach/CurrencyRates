using System.Xml;
using CurrencyRates.Entities;
using CurrencyRates.Models;
using CurrencyRates.Services;
using CurrencyRates.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Syncfusion.XlsIO.Implementation.XmlSerialization;
using CurrencyRates.Services.Interfaces;
using AngleSharp.Html;

namespace CurrencyRates.Controllers
{
    [Route("api/currencies")]
    [ApiController]
    public class CurrencyController : ControllerBase
    {
        private readonly Db db;
        private readonly ICurrencyService currencyService;
        

        public CurrencyController(DbContext db, ICurrencyService currencyService)
        {
            this.db = (Db)db;
            this.currencyService = currencyService;
        }

        /// <summary>
        /// Updates exchange rates of all currencies in the db with freshly fetched data from CNB API
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("updateAllExchangeRates")]
        public async Task<IActionResult> UpdateAllExchangeRates()
        {
            await this.currencyService.UpdateAllExchangeRates();

            return Ok(new { message = "Exchange rates updated successfully." });
        }


        /// <summary>
        /// Updates exchange rate of single given currency in the db with freshly fetched data from CNB API
        /// </summary>
        [HttpPost]
        [Route("updateExchangeRate")]
        public async Task<IActionResult> UpdateAllExchangeRates(int currencyId)
        {
            var currency = await db.Currencies.FirstOrDefaultAsync(c => c.Id == currencyId);
            if (currency == null)
            {
                return NotFound(new { error = $"Měna s Id {currencyId} nebyla v databázi nalezena" });
            }

            await this.currencyService.UpdateExchangeRateToCZK(currency);

            return Ok(new { message = $"Exchange rate of {currency.Title} updated successfully" });
        }


    }
}
