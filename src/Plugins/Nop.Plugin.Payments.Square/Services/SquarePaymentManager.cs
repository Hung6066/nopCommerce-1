﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Plugin.Payments.Square.Domain;
using Nop.Services.Configuration;
using Nop.Services.Logging;
using Square;
using Square.Exceptions;
using Square.Models;

namespace Nop.Plugin.Payments.Square.Services
{
    /// <summary>
    /// Represents the Square payment manager
    /// </summary>
    public class SquarePaymentManager
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly IWorkContext _workContext;
        private readonly SquareAuthorizationHttpClient _squareAuthorizationHttpClient;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public SquarePaymentManager(ILogger logger,
            IWorkContext workContext,
            SquareAuthorizationHttpClient squareAuthorizationHttpClient,
            ISettingService settingService,
            IStoreContext storeContext)
        {
            _logger = logger;
            _workContext = workContext;
            _squareAuthorizationHttpClient = squareAuthorizationHttpClient;
            _settingService = settingService;
            _storeContext = storeContext;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Create the API configuration
        /// </summary>
        /// <param name="storeId">Store identifier for which configuration should be loaded</param>
        /// <returns>The API Configuration</returns>
        public SquareClient CreateClient(int storeId)
        {
            var settings = _settingService.LoadSetting<SquarePaymentSettings>(storeId);

            //validate access token
            if (settings.UseSandbox && string.IsNullOrEmpty(settings.AccessToken))
                throw new NopException("Sandbox access token should not be empty");

            string token = settings.AccessToken;
            global::Square.Environment environment = global::Square.Environment.Sandbox;

            if (!settings.UseSandbox)
            {
                environment = global::Square.Environment.Production;
            }

            SquareClient squareClient = new SquareClient.Builder()
                .Environment(environment)
                .AccessToken(token)
                .Build();

            return squareClient;
        }

        #endregion

        #region Methods

        #region Common

        /// <summary>
        /// Get active business locations
        /// </summary>
        /// <param name="storeId">Store identifier for which locations should be loaded</param>
        /// <returns>List of location</returns>
        public IList<Location> GetActiveLocations(int storeId)
        {
            try
            {
                //create location API
                var client = CreateClient(storeId);
                var locationsApi = client.LocationsApi;

                //get list of all locations
                var listLocationsResponse = locationsApi.ListLocations();
                if (listLocationsResponse == null)
                    throw new NopException("No service response");

                //check whether there are errors in the service response
                if (listLocationsResponse.Errors?.Any() ?? false)
                {
                    var errorsMessage = string.Join(";", listLocationsResponse.Errors.Select(error => error.ToString()));
                    throw new NopException($"There are errors in the service response. {errorsMessage}");
                }

                //filter active locations and locations that can process credit cards
                var activeLocations = listLocationsResponse.Locations?.Where(location => location?.Status == SquarePaymentDefaults.LOCATION_STATUS_ACTIVE
                    && (location.Capabilities?.Contains(SquarePaymentDefaults.LOCATION_CAPABILITIES_PROCESSING) ?? false)).ToList();
                if (!activeLocations?.Any() ?? true)
                    throw new NopException("There are no active locations for the account");

                return activeLocations;
            }
            catch (Exception exception)
            {
                //log full error
                _logger.Error($"Square payment error: {exception.Message}.", exception, _workContext.CurrentCustomer);

                return new List<Location>();
            }
        }

        /// <summary>
        /// Get customer by identifier
        /// </summary>
        /// <param name="customerId">Customer ID</param>
        /// <param name="storeId">Store identifier for which customer should be loaded</param>
        /// <returns>Customer</returns>
        public Customer GetCustomer(string customerId, int storeId)
        {
            try
            {
                //whether passed customer identifier exists
                if (string.IsNullOrEmpty(customerId))
                    return null;

                //create customer API
                var client = CreateClient(storeId);
                var customersApi = client.CustomersApi;

                //get customer by identifier
                var retrieveCustomerResponse = customersApi.RetrieveCustomer(customerId);
                if (retrieveCustomerResponse == null)
                    throw new NopException("No service response");

                //check whether there are errors in the service response
                if (retrieveCustomerResponse.Errors?.Any() ?? false)
                {
                    var errorsMessage = string.Join(";", retrieveCustomerResponse.Errors.Select(error => error.ToString()));
                    throw new NopException($"There are errors in the service response. {errorsMessage}");
                }

                return retrieveCustomerResponse.Customer;
            }
            catch (Exception exception)
            {
                //log full error
                _logger.Error($"Square payment error: {exception.Message}.", exception, _workContext.CurrentCustomer);

                return null;
            }
        }

        /// <summary>
        /// Create the new customer
        /// </summary>
        /// <param name="customerRequest">Request parameters to create customer</param>
        /// <param name="storeId">Store identifier for which customer should be created</param>
        /// <returns>Customer</returns>
        public Customer CreateCustomer(CreateCustomerRequest customerRequest, int storeId)
        {
            try
            {
                //create customer API
                var client = CreateClient(storeId);
                var customersApi = client.CustomersApi;

                //create the new customer
                var createCustomerResponse = customersApi.CreateCustomer(customerRequest);
                if (createCustomerResponse == null)
                    throw new NopException("No service response");

                //check whether there are errors in the service response
                if (createCustomerResponse.Errors?.Any() ?? false)
                {
                    var errorsMessage = string.Join(";", createCustomerResponse.Errors.Select(error => error.ToString()));
                    throw new NopException($"There are errors in the service response. {errorsMessage}");
                }

                return createCustomerResponse.Customer;
            }
            catch (Exception exception)
            {
                //log full error
                _logger.Error($"Square payment error: {exception.Message}.", exception, _workContext.CurrentCustomer);

                return null;
            }
        }

        /// <summary>
        /// Create the new card of the customer
        /// </summary>
        /// <param name="customerId">Customer ID</param>
        /// <param name="cardRequest">Request parameters to create card of the customer</param>
        /// <param name="storeId">Store identifier for which customer card should be created</param>
        /// <returns>Card</returns>
        public Card CreateCustomerCard(string customerId, CreateCustomerCardRequest cardRequest, int storeId)
        {
            try
            {
                //create customer API
                var client = CreateClient(storeId);
                var customersApi = client.CustomersApi;

                //create the new card of the customer
                var createCustomerCardResponse = customersApi.CreateCustomerCard(customerId, cardRequest);
                if (createCustomerCardResponse == null)
                    throw new NopException("No service response");

                //check whether there are errors in the service response
                if (createCustomerCardResponse.Errors?.Any() ?? false)
                {
                    var errorsMessage = string.Join(";", createCustomerCardResponse.Errors.Select(error => error.ToString()));
                    throw new NopException($"There are errors in the service response. {errorsMessage}");
                }

                return createCustomerCardResponse.Card;
            }
            catch (Exception exception)
            {
                //log full error
                _logger.Error($"Square payment error: {exception.Message}.", exception, _workContext.CurrentCustomer);

                return null;
            }
        }

        #endregion

        #region Payment workflow

        /// <summary>
        /// Get transaction by transaction identifier
        /// </summary>
        /// <param name="transactionId">Transaction ID</param>
        /// <param name="storeId">Store identifier for which transaction should be loaded</param>
        /// <returns>Transaction and/or errors if exist</returns>
        public (Transaction, string) GetTransaction(string transactionId, int storeId)
        {
            try
            {
                var settings = _settingService.LoadSetting<SquarePaymentSettings>(storeId);

                //try to get the selected location
                var selectedLocation = GetActiveLocations(storeId).FirstOrDefault(location => location.Id.Equals(settings.LocationId));
                if (selectedLocation == null)
                    throw new NopException("Location is a required parameter for payment requests");

                //create transaction API
                var client = CreateClient(storeId);
                var transactionsApi = client.TransactionsApi;

                //get transaction by identifier
                var retrieveTransactionResponse = transactionsApi.RetrieveTransaction(selectedLocation.Id, transactionId);
                if (retrieveTransactionResponse == null)
                    throw new NopException("No service response");

                //check whether there are errors in the service response
                if (retrieveTransactionResponse.Errors?.Any() ?? false)
                {
                    var errorsMessage = string.Join(";", retrieveTransactionResponse.Errors.Select(error => error.ToString()));
                    throw new NopException($"There are errors in the service response. {errorsMessage}");
                }

                return (retrieveTransactionResponse.Transaction, null);
            }
            catch (Exception exception)
            {
                //log full error
                var errorMessage = exception.Message;
                _logger.Error($"Square payment error: {errorMessage}.", exception, _workContext.CurrentCustomer);

                if (exception is ApiException apiException)
                {
                    //try to get error details
                    var response = JsonConvert.DeserializeObject<RetrieveTransactionResponse>(apiException.Message) as RetrieveTransactionResponse;
                    if (response?.Errors?.Any() ?? false)
                        errorMessage = string.Join(";", response.Errors.Select(error => error.Detail));
                }

                return (null, errorMessage);
            }
        }


        /// <summary>
        /// Capture authorized transaction
        /// </summary>
        /// <param name="transactionId">Transaction ID</param>
        /// <param name="storeId">Store identifier for which transaction should be captured</param>
        /// <returns>True if the transaction successfully captured; otherwise false. And/or errors if exist</returns>
        public (bool, string) CaptureTransaction(string transactionId, int storeId)
        {
            try
            {
                var settings = _settingService.LoadSetting<SquarePaymentSettings>(storeId);

                //try to get the selected location
                var selectedLocation = GetActiveLocations(storeId).FirstOrDefault(location => location.Id.Equals(settings.LocationId));
                if (selectedLocation == null)
                    throw new NopException("Location is a required parameter for payment requests");

                //create transaction API
                var client = CreateClient(storeId);
                var transactionsApi = client.TransactionsApi;

                //capture transaction by identifier
                var captureTransactionResponse = transactionsApi.CaptureTransaction(selectedLocation.Id, transactionId);
                if (captureTransactionResponse == null)
                    throw new NopException("No service response");

                //check whether there are errors in the service response
                if (captureTransactionResponse.Errors?.Any() ?? false)
                {
                    var errorsMessage = string.Join(";", captureTransactionResponse.Errors.Select(error => error.ToString()));
                    throw new NopException($"There are errors in the service response. {errorsMessage}");
                }

                //if there are no errors in the response, transaction was successfully captured
                return (true, null);
            }
            catch (Exception exception)
            {
                //log full error
                var errorMessage = exception.Message;
                _logger.Error($"Square payment error: {errorMessage}.", exception, _workContext.CurrentCustomer);

                if (exception is ApiException apiException)
                {
                    //try to get error details
                    var response = JsonConvert.DeserializeObject<CaptureTransactionResponse>(apiException.Message) as CaptureTransactionResponse;
                    if (response?.Errors?.Any() ?? false)
                        errorMessage = string.Join(";", response.Errors.Select(error => error.Detail));
                }

                return (false, errorMessage);
            }
        }

        /// <summary>
        /// Void authorized transaction
        /// </summary>
        /// <param name="transactionId">Transaction ID</param>
        /// <param name="storeId">Store identifier for which transaction should be voided</param>
        /// <returns>True if the transaction successfully voided; otherwise false. And/or errors if exist</returns>
        public (bool, string) VoidTransaction(string transactionId, int storeId)
        {
            try
            {
                var settings = _settingService.LoadSetting<SquarePaymentSettings>(storeId);

                //try to get the selected location
                var selectedLocation = GetActiveLocations(storeId).FirstOrDefault(location => location.Id.Equals(settings.LocationId));
                if (selectedLocation == null)
                    throw new NopException("Location is a required parameter for payment requests");

                //create transaction API
                var client = CreateClient(storeId);
                var transactionsApi = client.TransactionsApi;

                //void transaction by identifier
                var voidTransactionResponse = transactionsApi.VoidTransaction(selectedLocation.Id, transactionId);
                if (voidTransactionResponse == null)
                    throw new NopException("No service response");

                //check whether there are errors in the service response
                if (voidTransactionResponse.Errors?.Any() ?? false)
                {
                    var errorsMessage = string.Join(";", voidTransactionResponse.Errors.Select(error => error.ToString()));
                    throw new NopException($"There are errors in the service response. {errorsMessage}");
                }

                //if there are no errors in the response, transaction was successfully voided
                return (true, null);
            }
            catch (Exception exception)
            {
                //log full error
                var errorMessage = exception.Message;
                _logger.Error($"Square payment error: {errorMessage}.", exception, _workContext.CurrentCustomer);

                if (exception is ApiException apiException)
                {
                    //try to get error details
                    var response = JsonConvert.DeserializeObject<VoidTransactionResponse>(apiException.Message) as VoidTransactionResponse;
                    if (response?.Errors?.Any() ?? false)
                        errorMessage = string.Join(";", response.Errors.Select(error => error.Detail));
                }

                return (false, errorMessage);
            }
        }

        /// <summary>
        /// Create a refund of the transaction
        /// </summary>
        /// <param name="transactionId">Transaction ID</param>
        /// <param name="refundRequest">Request parameters to create refund</param>
        /// <param name="storeId">Store identifier for which refund should be created</param>
        /// <returns>Refund and/or errors if exist</returns>
        public (Refund, string) CreateRefund(string transactionId, CreateRefundRequest refundRequest, int storeId)
        {
            try
            {
                var settings = _settingService.LoadSetting<SquarePaymentSettings>(storeId);

                //try to get the selected location
                var selectedLocation = GetActiveLocations(storeId).FirstOrDefault(location => location.Id.Equals(settings.LocationId));
                if (selectedLocation == null)
                    throw new NopException("Location is a required parameter for payment requests");

                //create transaction API
                var client = CreateClient(storeId);
                var transactionsApi = client.TransactionsApi;

                //create refund
                var createRefundResponse = transactionsApi.CreateRefund(selectedLocation.Id, transactionId, refundRequest);
                if (createRefundResponse == null)
                    throw new NopException("No service response");

                //check whether there are errors in the service response
                if (createRefundResponse.Errors?.Any() ?? false)
                {
                    var errorsMessage = string.Join(";", createRefundResponse.Errors.Select(error => error.ToString()));
                    throw new NopException($"There are errors in the service response. {errorsMessage}");
                }

                return (createRefundResponse.Refund, null);
            }
            catch (Exception exception)
            {
                //log full error
                var errorMessage = exception.Message;
                _logger.Error($"Square payment error: {errorMessage}.", exception, _workContext.CurrentCustomer);

                if (exception is ApiException apiException)
                {
                    //try to get error details
                    var response = JsonConvert.DeserializeObject<CreateRefundResponse>(apiException.Message) as CreateRefundResponse;
                    if (response?.Errors?.Any() ?? false)
                        errorMessage = string.Join(";", response.Errors.Select(error => error.Detail));
                }

                return (null, errorMessage);
            }
        }

        #endregion

        #region OAuth2 authorization

        /// <summary>
        /// Generate URL for the authorization permissions page
        /// </summary>
        /// <param name="storeId">Store identifier for which authorization url should be created</param>
        /// <returns>URL</returns>
        public string GenerateAuthorizeUrl(int storeId)
        {
            var serviceUrl = $"{_squareAuthorizationHttpClient.BaseAddress}authorize";

            //list of all available permission scopes
            var permissionScopes = new List<string>
            {
                //GET endpoints related to a merchant's business and location entities.
                "MERCHANT_PROFILE_READ",

                //GET endpoints related to transactions and refunds.
                "PAYMENTS_READ",

                //POST, PUT, and DELETE endpoints related to transactions and refunds
                "PAYMENTS_WRITE",

                //GET endpoints related to customer management.
                "CUSTOMERS_READ",

                //POST, PUT, and DELETE endpoints related to customer management.
                "CUSTOMERS_WRITE",

                //GET endpoints related to settlements (deposits).
                "SETTLEMENTS_READ",

                //GET endpoints related to a merchant's bank accounts.
                "BANK_ACCOUNTS_READ",

                //GET endpoints related to a merchant's item library.
                "ITEMS_READ",

                //POST, PUT, and DELETE endpoints related to a merchant's item library.
                "ITEMS_WRITE",

                //GET endpoints related to a merchant's orders.
                "ORDERS_READ",

                //POST, PUT, and DELETE endpoints related to a merchant's orders.
                "ORDERS_WRITE",

                //GET endpoints related to employee management.
                "EMPLOYEES_READ",

                //POST, PUT, and DELETE endpoints related to employee management.
                "EMPLOYEES_WRITE",

                //GET endpoints related to employee timecards.
                "TIMECARDS_READ",

                //POST, PUT, and DELETE endpoints related to employee timecards.
                "TIMECARDS_WRITE"
            };

            //request all of the permissions
            var requestingPermissions = string.Join(" ", permissionScopes);

            var settings = _settingService.LoadSetting<SquarePaymentSettings>(storeId);

            //create query parameters for the request
            var queryParameters = new Dictionary<string, string>
            {
                //The application ID.
                ["client_id"] = settings.ApplicationId,

                //Indicates whether you want to receive an authorization code ("code") or an access token ("token").
                ["response_type"] = "code",

                //A space-separated list of the permissions your application is requesting. 
                ["scope"] = requestingPermissions,

                //The locale to present the Permission Request form in. Currently supported values are en-US, en-CA, es-US, fr-CA, and ja-JP.
                //["locale"] = string.Empty,

                //If "false", the Square merchant must log in to view the Permission Request form, even if they already have a valid user session.
                ["session"] = "false",

                //Include this parameter and verify its value to help protect against cross-site request forgery.
                ["state"] = settings.AccessTokenVerificationString,

                //The ID of the subscription plan to direct the merchant to sign up for, if any.
                //You can provide this parameter with no value to give a merchant the option to cancel an active subscription.
                //["plan_id"] = string.Empty,
            };

            //return generated URL
            return QueryHelpers.AddQueryString(serviceUrl, queryParameters);
        }

        /// <summary>
        /// Exchange the authorization code for an access token
        /// </summary>
        /// <param name="authorizationCode">Authorization code</param>
        /// <param name="storeId">Store identifier for which access token should be obtained</param>
        /// <returns>Access and refresh tokens</returns>
        public (string AccessToken, string RefreshToken) ObtainAccessToken(string authorizationCode, int storeId)
        {
            return _squareAuthorizationHttpClient.ObtainAccessTokenAsync(authorizationCode, storeId).Result;
        }

        /// <summary>
        /// Renew the expired access token
        /// </summary>
        /// <param name="storeId">Store identifier for which access token should be updated</param>
        /// <returns>Access and refresh tokens</returns>
        public (string AccessToken, string RefreshToken) RenewAccessToken(int storeId)
        {
            return _squareAuthorizationHttpClient.RenewAccessTokenAsync(storeId).Result;
        }

        /// <summary>
        /// Revoke all access tokens
        /// </summary>
        /// <param name="storeId">Store identifier for which access token should be revoked</param>
        /// <returns>True if tokens were successfully revoked; otherwise false</returns>
        public bool RevokeAccessTokens(int storeId)
        {
            return _squareAuthorizationHttpClient.RevokeAccessTokensAsync(storeId).Result;
        }

        #endregion

        #endregion
    }
}