﻿using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace LrnAirdropContract
{
    public class LrnAirdropContract : SmartContract
    {
        public static readonly byte[] SuperAdmin = "AR7W16oCGSyKF4ebGjod9EFFwTUyRPZV9o".ToScriptHash();
        private static readonly byte INVOCATION_TRANSACTION_TYPE = 0xd1;
        private const int FIRST_AIRDROP_START_TIME = 1530720000;//2018-07-05 00:00:00
        private const int SECOND_AIRDROP_START_TIME = 1536076800;//2018-09-05 00:00:00
        private const int THIRD_AIRDROP_START_TIME = 1541347200;//2018-11-05 00:00:00
        private const Int64 TOTAL_AMOUNT_PER_PHASE = 2790152100000000;
        private const Int64 TOTAL_AIRDROP_AMOUNT = 8370456300000000;
        private const string AIR_DROP_SUPPLY = "airdropSupply";
        private const string LAST_WITHDRAW_TIME = "lastWithdrawTime";
        private const int SECONDS_PER_DAY = 86400;
        private const int PERIOD = 730;
        public static readonly byte[] FIRST_PHASE_PREFIX = "firstPhase".AsByteArray();
        public static readonly byte[] SECOND_PHASE_PREFIX = "secondPhase".AsByteArray();
        public static readonly byte[] THIRD_PHASE_PREFIX = "thirdPhase".AsByteArray();


        [Appcall("06fa8be9b6609d963e8fc63977b9f8dc5f10895f")]
        static extern object CallLrn(string method, object[] arr);

        [DisplayName("withdraw")]
        public static event Action<byte[], BigInteger> Withdrew;

        /// <summary>
        ///   This smart contract is designed to airdrop and withdraw according to time.
        ///   Parameter List: 0710
        ///   Return List: 05
        /// </summary>
        /// <param name="operation">
        ///   The method being invoked.
        /// </param>
        /// <param name="args">
        ///   Optional input parameters. 
        /// </param>
        public static Object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                if (Runtime.CheckWitness(SuperAdmin)) return true;

                Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
                var type = tx.Type;

                if (type != INVOCATION_TRANSACTION_TYPE) return false;

                var invocationTransaction = (InvocationTransaction)tx;
                if (invocationTransaction.Script.Length != 53)
                {
                    Runtime.Log("invocationTransaction.Script.Length illegal!");
                    return false;
                }

                if (invocationTransaction.Script[0] != 0x14) return false;

                if (invocationTransaction.Script.Range(21,29) != (new byte[] { 0x51, 0xc1, 0x09 }).Concat("withdraw".AsByteArray()).Concat(new byte[]{0x67}).Concat(ExecutionEngine.ExecutingScriptHash))
                {
                    Runtime.Log("invocationTransaction.Script illegal!");
                    return false;
                }

                return true;
            }

            if (Runtime.Trigger == TriggerType.Application)
            {
                if (operation == "deploy")
                {
                    if (!Runtime.CheckWitness(SuperAdmin)) return false;
                    Storage.Put(Storage.CurrentContext, AIR_DROP_SUPPLY, 0);
                    return true;
                }
                if (operation == "deposit")
                {
                    return Deposit(args);
                }
                if (operation == "withdraw")
                {
                    if (args.Length != 1) return false;
                    byte[] to = (byte[])args[0];
                    return Withdraw(to);
                }
                if (operation == "queryAirDropSupply")
                {
                    return Storage.Get(Storage.CurrentContext, AIR_DROP_SUPPLY).AsBigInteger();
                }
                if (operation == "queryAirDropBalance")
                {
                    if (args.Length != 1) return false;
                    byte[] accountScriptHash = (byte[])args[0];
                    return Storage.Get(Storage.CurrentContext, accountScriptHash).AsBigInteger();
                }
                if (operation == "queryAvailableBalance")
                {
                    if (args.Length != 1) return false;
                    byte[] accountScriptHash = (byte[])args[0];
                    return CalcTotalAvailableAmount(accountScriptHash);
                }
                if (operation == "setWithdrawSwitch")
                {
                    if (!Runtime.CheckWitness(SuperAdmin)) return false;
                    if (args.Length != 1) return false;
                    if((string)args[0] == "on")
                    {
                        Storage.Put(Storage.CurrentContext, "WithdrawSwitch", 1);
                    } else
                    {
                        Storage.Put(Storage.CurrentContext, "WithdrawSwitch", 0);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        ///   deposit the amount to an account.
        /// </summary>
        /// <param name="args">
        ///   The contract invoker.
        /// </param>
        /// <returns>
        ///   Transaction Successful?
        /// </returns>
        public static bool Deposit(object[] args)
        {
            if (!Runtime.CheckWitness(SuperAdmin)) return false;

            if (args.Length != 2) return false;
            byte[] account = (byte[])args[0];
            if (account.Length != 20) return false;

            BigInteger depositAmount = (BigInteger)args[1];
            if (depositAmount <= 0 || depositAmount > TOTAL_AMOUNT_PER_PHASE) return false;
            BigInteger supply = Storage.Get(Storage.CurrentContext, AIR_DROP_SUPPLY).AsBigInteger();
            if ((supply + depositAmount) > TOTAL_AIRDROP_AMOUNT) return false;

            Storage.Put(Storage.CurrentContext, account, depositAmount);
            Storage.Put(Storage.CurrentContext, AIR_DROP_SUPPLY, supply + depositAmount);

            return true;
        }

        /// <summary>
        ///   Withdraw the available amount to the account.
        /// </summary>
        /// <param name="account">
        ///   The account to withdraw.
        /// </param>
        /// <returns>
        ///   Transaction Successful?
        /// </returns>
        public static bool Withdraw(byte[] account)
        {
            BigInteger withdrawSwitch = Storage.Get(Storage.CurrentContext, "WithdrawSwitch").AsBigInteger();
            if (withdrawSwitch == 0) return false;

            BigInteger firstAvailbableAmount = CalcAvailableAmount(account, FIRST_PHASE_PREFIX);
            BigInteger secondAvailbableAmount = CalcAvailableAmount(account, SECOND_PHASE_PREFIX);
            BigInteger thirdAvailbableAmount = CalcAvailableAmount(account, THIRD_PHASE_PREFIX);
            BigInteger withdrawAmount = firstAvailbableAmount + secondAvailbableAmount + thirdAvailbableAmount;

            if (withdrawAmount < 1) return false;

            byte[] from = Neo.SmartContract.Framework.Services.System.ExecutionEngine.ExecutingScriptHash;
            // call lrn transfer
            byte[] rt = (byte[])CallLrn("transfer", new object[] { from, account, withdrawAmount });
            bool succ = rt.AsBigInteger() == 1;
            if (succ)
            {
                if(firstAvailbableAmount > 0)
                {
                    BigInteger balance = Storage.Get(Storage.CurrentContext, FIRST_PHASE_PREFIX.Concat(account)).AsBigInteger();
                    Storage.Put(Storage.CurrentContext, account, balance - firstAvailbableAmount);
                }
                if (secondAvailbableAmount > 0)
                {
                    BigInteger balance = Storage.Get(Storage.CurrentContext, SECOND_PHASE_PREFIX.Concat(account)).AsBigInteger();
                    Storage.Put(Storage.CurrentContext, account, balance - secondAvailbableAmount);
                }
                if (thirdAvailbableAmount > 0)
                {
                    BigInteger balance = Storage.Get(Storage.CurrentContext, THIRD_PHASE_PREFIX.Concat(account)).AsBigInteger();
                    Storage.Put(Storage.CurrentContext, account, balance - thirdAvailbableAmount);
                }

                BigInteger now = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
                Storage.Put(Storage.CurrentContext, account.Concat(LAST_WITHDRAW_TIME.AsByteArray()), now);
                Withdrew(account, withdrawAmount);
            }
            return true;
        }

        /// <summary>
        ///  Calculate the available amount to withdraw for the account.
        /// </summary>
        /// <param name="account">
        ///  the account to withdraw
        /// </param>
        /// <param name="phase">
        ///  The phase of the airdrop.
        /// </param>
        /// <returns>
        ///  available amount to withdraw per phase
        /// </returns>
        public static BigInteger CalcAvailableAmount(byte[] account, byte[] phase)
        {
            if (account.Length != 20) return 0;
            BigInteger amount = Storage.Get(Storage.CurrentContext, phase.Concat(account)).AsBigInteger();
            if (amount < 1) return 0;
            BigInteger now = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            if (now < FIRST_AIRDROP_START_TIME) return 0;
            BigInteger lastWithdrawTime = Storage.Get(Storage.CurrentContext, account.Concat(LAST_WITHDRAW_TIME.AsByteArray())).AsBigInteger();
            BigInteger holdDays = 0;
            BigInteger totalAvailableAmount = 0;
            BigInteger startTime = 0;
            if (phase == FIRST_PHASE_PREFIX)
            {
                startTime = FIRST_AIRDROP_START_TIME;
            }
            if (phase == SECOND_PHASE_PREFIX)
            {
                startTime = SECOND_AIRDROP_START_TIME;
            }
            if (phase == THIRD_PHASE_PREFIX)
            {
                startTime = THIRD_AIRDROP_START_TIME;
            }

            if (lastWithdrawTime > FIRST_AIRDROP_START_TIME)
            {
                holdDays = (now - lastWithdrawTime) / SECONDS_PER_DAY + 1;
            } else
            {
                holdDays = (now - startTime) / SECONDS_PER_DAY + 1;
            }

            BigInteger availbableAmount = amount * holdDays / PERIOD;
            return availbableAmount;
        }

        /// <summary>
        ///  Calculate the available amount to withdraw for the account.
        /// </summary>
        /// <param name="account">
        ///  the account to withdraw
        /// </param>
        /// <returns>
        ///  available amount to withdraw
        /// </returns>
        public static BigInteger CalcTotalAvailableAmount(byte[] account)
        {
            BigInteger firstAvailbableAmount = CalcAvailableAmount(account, FIRST_PHASE_PREFIX);
            BigInteger secondAvailbableAmount = CalcAvailableAmount(account, SECOND_PHASE_PREFIX);
            BigInteger thirdAvailbableAmount = CalcAvailableAmount(account, THIRD_PHASE_PREFIX);
            BigInteger availableAmount = firstAvailbableAmount + secondAvailbableAmount + thirdAvailbableAmount;
            return availableAmount;
        }
    }
}
