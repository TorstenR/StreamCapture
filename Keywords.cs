using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace StreamCapture
{
    public class Keywords
    {
        Dictionary<string, KeywordInfo> keywordDict;

        public Keywords(IConfiguration configuration)
        {
            try
            {
                keywordDict = JsonConvert.DeserializeObject<Dictionary<string, KeywordInfo>>(File.ReadAllText("keywords.json"));
            }
            catch(Exception e)
            {
                //Send alert mail
                string body="Keyword load failed with Exception "+e.Message;
                body=body+"\n"+e.StackTrace;
                new Mailer().SendErrorMail(configuration,"ERROR: keywords.json Exception ("+e.Message+")",body);

                throw new Exception("Problem deserializing keywords.json",e);
            }                
        }

        public KeywordInfo[] GetKeywordArray()
        {
            return keywordDict.Values.ToArray();
        }

        //Given a show name, see if there's a match in any of the keywords
        public Tuple<KeywordInfo,int> FindMatch(string showName)
        {
            KeywordInfo keywordInfo = null;

            //Go through keywords seeing if there's a match
            KeyValuePair<string, KeywordInfo>[] kvpArray = keywordDict.ToArray();
            int kvpIdx;
            for(kvpIdx=0;kvpIdx<kvpArray.Length;kvpIdx++)
            {
                string strKeywords = kvpArray[kvpIdx].Value.keywords;
                string[] kArray = strKeywords.Split(',');

                for (int i = 0; i < kArray.Length; i++)
                {
                    if (showName.ToLower().Contains(kArray[i].ToLower()))
                    {
                        string[] excludeArray = kvpArray[kvpIdx].Value.exclude.Split(',');

                        //Make sure no keywords to exclude are found
                        bool excludedFlag=false;
                        for(int e=0;e<excludeArray.Length;e++)
                        {
                            if(excludeArray[e].Length>0)
                            {
                                if (showName.ToLower().Contains(excludeArray[e].ToLower()))
                                    excludedFlag=true;
                            }
                        }

                        if(!excludedFlag)
                        {
                            keywordInfo = kvpArray[kvpIdx].Value;
                            return new Tuple<KeywordInfo,int>(keywordInfo,kvpIdx);
                        }
                    }
                }
            }

            return null;
        }
    }
}
 