﻿using Reclamation.Core;
using Reclamation.TimeSeries.Hydromet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Reclamation.TimeSeries.Reports
{
    public class YakimaStatusReport
    {
        public YakimaStatusReport()
        {
        }

        string[] resList = { "kee", "kac", "cle", "bum", "rim", "clr" };
        string[] major_qc = { "ktcw", "rozw", "tiew", "rscw", "sncw" };
        string[] trib_qc = { "ktcw", "sncw", "rscw", "tiew", "rozw"};
        string[] others_above_parker = { "wesw", "nscw" };
        double[] resCapacity ={157800,239000,436900,33970,198000,-1};
        double[] res_af = { 0, 0, 0, 0, 0, 0 };
        double[] res_af2 = { 0, 0, 0, 0, 0, 0 }; // previous day same hour
        double[] res_q = { 0, 0, 0, 0, 0 ,0};
        double totalCapacity= 1065400;
        double total_af = 0;
        double reservoir_total_release = 0;
        double total_in = 0;
        double major_qc_total = 0;
        double above_parker_qc = 0;
        double trib_qc_total = 0;
        double parw_q = double.MinValue;
        /// <summary>
        /// Creates and returns the report.
        /// </summary>
        /// <returns></returns>
        public string Create(DateTime t, int year1=0, int year2=0) // default 8am.
        {
            string rval = GetTemplate();
            //13-OCT-2016  09:12:35
            var fmt = "dd-MMM-yyyy  HH:mm:ss";
            rval = rval.Replace(fmt, t.ToString(fmt));
            rval = rval.Replace("HH:mm", t.ToString("HH:mm"));


            res_af = Array.ConvertAll(res_af,x => x=double.MinValue);
            res_af2 = Array.ConvertAll(res_af, x => x = double.MinValue);
            res_q = Array.ConvertAll(res_af, x => x = double.MinValue);
            
            DateTime t1 =t.AddDays(-1);
            DateTime t2 = t;
            HydrometDataCache c = new HydrometDataCache();
            
            HydrometInstantSeries.Cache.Add(this.yakima_data, t1, t2, HydrometHost.Yakima, TimeInterval.Irregular);
            
            foreach (var cbtt in resList)
            {
              rval = ProcessParameter(rval ,t, cbtt, "fb");
              rval = ProcessParameter(rval, t, cbtt, "af");
              rval = ProcessParameter(rval, t, cbtt, "q");
            }

            rval = ReplaceSymbol(rval, "%total_af", total_af);
            double total_pct = total_af / totalCapacity * 100.0;
            rval = ReplaceSymbol(rval, "%total_pct", total_pct);
            rval = ReplaceSymbol(rval, "%total_q", reservoir_total_release);

            // compute inflows.
            for (int i = 0; i < resList.Length; i++)
            {
                if (resList[i] == "clr")
                    continue; // no contents
                var qu = (res_af[i]-res_af2[i]) / 1.9835 + res_q[i];
                rval = ReplaceSymbol(rval, "%" + resList[i] + "_in", qu);
                total_in += qu;
            }
            rval = ReplaceSymbol(rval, "%total_in", total_in);
            foreach (var canal in DataSubset("qc"))
            {
                var cbtt = canal.Substring(0, canal.IndexOf(" "));
                rval = ProcessParameter(rval, t, cbtt, "qc");
            }

            double others = ComputeOthersAboveParker(t1);
            rval = ReplaceSymbol(rval, "%major_qc", major_qc_total);
            rval = ReplaceSymbol(rval, "%other_qc", others);
            above_parker_qc += others + major_qc_total;
            rval = ReplaceSymbol(rval, "%parker_qc", above_parker_qc);
            

            foreach (var river in DataSubset("q"))
            {
                var cbtt = river.Substring(0, river.IndexOf(" "));
                if( !resList.Contains(cbtt)) // reservoir allready processed
                  rval = ProcessParameter(rval, t, cbtt, "q");
            }
            
            // unregulated tributary and return flows above parker.
            var above_parker = trib_qc_total + others + parw_q - reservoir_total_release;

             rval = ReplaceSymbol(rval, "%trib_parw", above_parker);

             rval = rval + "\r\nOPERATIONAL COMMENTS:  ";
             if (year1 > 0 && year2 > 0)
             {
                 var t1a = new DateTime(year1 - 1, 10, 1);
                 var t2a = new DateTime(year2, 9, 30);

                 double avgPct = MultiYearAvg(t1,t1a, t2a);


                 rval = rval + " Storage is " + avgPct.ToString("F1") + "% of average (" + year1
                      + ", " + year2 + ")."
                      + "\r\n---------------------";
             }

            return rval;

        }

        /// <summary>
        /// Compute/estimate smaller diversions above parker.
        /// This is done using factors that vary with time
        /// and  Daily data for WESW QJ, and NSCW QJ.
        /// </summary>
        /// <param name="t1"></param>
        /// <returns></returns>
        private static double ComputeOthersAboveParker(DateTime t)
        {

            HydrometDailySeries wesw = new HydrometDailySeries("wesw", "qj", HydrometHost.Yakima);
            HydrometDailySeries nscw = new HydrometDailySeries("nscw", "qj", HydrometHost.Yakima);

            DateTime t1 = t.Date;

            wesw.Read(t1, t1);
            nscw.Read(t1, t1);

            wesw.RemoveMissing();
            nscw.RemoveMissing();

            if( wesw.Count ==0 || nscw.Count == 0)
            {
                Logger.WriteLine("Missing data. Check wesw, nscw");
                return Point.MissingValueFlag;
            }


            var rval = GetValue(t1, wesw[0].Value, nscw[0].Value);
            return rval;
        }

        private static double GetValue(DateTime t1, double wesw, double nscw)
        {
            double rval = 200.0;
            var fn = Path.Combine(FileUtility.GetExecutableDirectory(),"YakimaOthersAboveParker.csv");
            CsvFile csv = new CsvFile(fn, CsvFile.FieldTypes.AutoDetect);

            for (int i = 0; i < csv.Rows.Count; i++)
            {
                var r = csv.Rows[i];
                var d = Convert.ToDateTime(r["DateTime"]);

                if (t1.Month == d.Month
                    && t1.Day == d.Day)
                {
                    double a1 =Convert.ToDouble( r[1]);
                    double a2 = Convert.ToDouble(r[2]);
                    double a3 = Convert.ToDouble(r[3]);
                    double a4 = Convert.ToDouble(r[4]);
                    double a5 = Convert.ToDouble(r[5]);
                    rval = (a1 + a2) * wesw + (a3 + a4 + a5) * nscw;
                }
            }
            return System.Math.Max(200, rval);
        }
        private static double MultiYearAvg(DateTime t,DateTime t1, DateTime t2)
        {
            var s = HydrometDailySeries.GetMultiYearAverage("sys", "af",
                HydrometHost.Yakima, t1, t2);
            

            DateTime t2000 = new DateTime(2000, t.Month, t.Day);
            int idx = s.IndexOf(t2000.Date);

            if (idx >= 0 && !s[idx].IsMissing)
            {
                var af = HydrometDailySeries.Read("sys", "af", t, t, HydrometHost.Yakima);
                af.RemoveMissing();
                if (af.Count == 1)
                {
                    var x = s[idx].Value;
                    x = af[0].Value / x * 100.0;
                    return x;
                }
            }

            return Reclamation.TimeSeries.Point.MissingValueFlag;

        }
        private string[] DataSubset(string pcode)
        {
            var query = from a in yakima_data
                        where a.IndexOf(" " + pcode) > 0
                        select a;
            var rval =  query.ToArray();
            return rval;
        }

        private string ProcessParameter(string txt, DateTime t,
            string cbtt, string pcode)
        {
            var x = GetValue(cbtt, pcode, t);
            int decimals =  ( pcode.Trim() == "fb") ? 2 : 0;
            int idx = Array.IndexOf(resList, cbtt);

            var rval =  ReplaceSymbol(txt, "%" + cbtt + "_" + pcode, x, decimals);

            if( pcode == "af" && !Point.IsMissingValue(x))
            {// also take care of percent full
                total_af += x;
                
                if( idx >=0)
                {
                    res_af[idx] = x;
                    double pct = 100.0 * x /resCapacity[idx];
                    rval = ReplaceSymbol(rval, "%" + cbtt + "_pct", pct, 0);
                   // get yesterdays storage
                    var x2 = GetValue(cbtt, pcode, t.AddDays(-1));
                    if (!Point.IsMissingValue(x2))
                    {
                        res_af2[idx] = x2;
                    }
                }
            }

            if (pcode == "q" && !Point.IsMissingValue(x))
            {
                if (idx >= 0)
                {
                    res_q[idx] = x;
                    reservoir_total_release += x;
                }
                if (cbtt == "parw")
                    parw_q = x;
            }

            if (pcode == "qc" && !Point.IsMissingValue(x))
            {
                if(major_qc.Contains(cbtt) )
                   major_qc_total += x;
                if (others_above_parker.Contains(cbtt))
                    above_parker_qc += x;

                if( trib_qc.Contains(cbtt))
                {
                    trib_qc_total += x;
                }
            }


            

            return rval;
        }
        
        /// <summary>
        /// replaces placholder symbol with formated value 
        /// </summary>
        private string ReplaceSymbol(string text, string symbol, 
                                     double val,int decimals=0)
        {
            var str = val.ToString("F" + decimals.ToString());
            if (decimals == 0)
                str += ".";

            if (str.Length > 30 || val == double.MinValue)
                str = "Error".PadLeft(symbol.Length);
            else
            {
                str = str.PadLeft(symbol.Length);
            }

            return text.Replace(symbol, str);
        }

        public double GetValue(string cbtt, string pcode, DateTime t)
        {
            var s = new HydrometInstantSeries(cbtt, pcode,HydrometHost.Yakima);
            //DateTime th = new DateTime(t.Year, t.Month, t.Day, hour, 0, 0);
            s.Read(t,t);
            
            if( s.Count >0 && !s[0].IsMissing)
            {
                return s[0].Value;
            }
            return Point.MissingValueFlag;
        }
        
         private string GetTemplate()
        {
            return File.ReadAllText(
                  Path.Combine(
                  FileUtility.GetExecutableDirectory(),
                  "YakimaStatusTemplate.txt")
                  );

        }
        private string[] yakima_data =
            new string[]{
"kee fb",
"kac fb",
"cle fb",
"bum fb",
"rim fb",
"clr fb",
"kee af",
"kac af",
"cle af",
"bum af",
"rim af",
"easw q",
"yumw q",
"umtw q",
"nacw q",
"parw q",
"yrpw q",
"kee q",
"kac q",
"cle q",
"bum q",
"rim q",
"ktcw qc",
"wopw qc",
"rzcw qc",
"nscw qc",
"sncw qc",
"rscw qc",
"tiew qc",
"rozw qc",
"wesw qc",
"chcw qc",
"kncw qc",
"tnaw q",
"yrww q",
"clfw q",
"ticw q",
"rbdw q",
"yrcw q"            };




    }
}
