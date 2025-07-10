using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using .Entities;

namespace .Services.Interfaces
{
    public interface ICurrencyService
    {

        Task UpdateAllExchangeRates();
        Task UpdateExchangeRateToCZK(Currency currency, string? cnbCSV = null);
        Task<string> FetchCSVFromCNBApi(DateTime? date = null);

        Task<decimal> PickExchangeRateFromCnbCSV(Currency currency, string? cnbCSV = null, DateTime? date = null);
    }
}
