﻿#region Copyright (C) 2017  Kevin  （OS系列开源项目）

/***************************************************************************
*　　	文件功能描述：消息对话事件句柄基类，主要声明相关事件
*
*　　	创建人： kevin
*       创建人Email：1985088337@qq.com
*    	创建日期：2017-1-13
*       
*****************************************************************************/

#endregion

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using OSS.Common.ComModels;
using OSS.Common.ComModels.Enums;
using OSS.Common.Encrypt;
using OSS.Common.Extention;
using OSS.SnsSdk.Msg.Wx.Mos;
using OSS.SocialSDK.WX.Msg.Mos;

namespace OSS.SnsSdk.Msg.Wx
{
    /// <summary>
    /// 消息处理基类
    ///  </summary>
    public class WxMsgHandler
    {
        protected readonly WxMsgServerConfig m_Config;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="mConfig"></param>
        protected WxMsgHandler(WxMsgServerConfig mConfig)
        {
            m_Config = mConfig;
        }

        #region   基础消息的事件列表

        #region 事件列表  普通消息

        /// <summary>
        /// 处理未知类型消息
        /// </summary>
        protected event Func<BaseRecMsg, BaseReplyMsg> UnknowHandler;

        /// <summary>
        /// 处理文本消息
        /// </summary>
        protected event Func<TextRecMsg, BaseReplyMsg> TextHandler;

        /// <summary>
        /// 处理图像消息
        /// </summary>
        protected event Func<ImageRecMsg, BaseReplyMsg> ImageHandler;

        /// <summary>
        /// 处理语音消息
        /// </summary>
        protected event Func<VoiceRecMsg, BaseReplyMsg> VoiceHandler;

        /// <summary>
        /// 处理视频/小视频消息
        /// </summary>
        protected event Func<VideoRecMsg, BaseReplyMsg> VideoHandler;

        /// <summary>
        /// 处理地理位置消息
        /// </summary>
        protected event Func<LocationRecMsg, BaseReplyMsg> LocationHandler;

        /// <summary>
        /// 处理链接消息
        /// </summary>
        protected event Func<LinkRecMsg, BaseReplyMsg> LinkHandler;

        #endregion

        #region 事件列表  动作事件消息


        /// <summary>
        /// 处理关注/取消关注事件
        /// </summary>
        protected event Func<SubscribeRecEventMsg, BaseReplyMsg> SubscribeEventHandler;

        /// <summary>
        /// 处理扫描带参数二维码事件
        /// </summary>
        protected event Func<SubscribeRecEventMsg, BaseReplyMsg> ScanEventHandler;

        /// <summary>
        /// 处理上报地理位置事件
        /// 不需要回复任何消息
        /// </summary>
        protected event Func<LocationRecEventMsg, NoneReplyMsg> LocationEventHandler;

        /// <summary>
        /// 处理点击菜单拉取消息时的事件推送
        /// </summary>
        protected event Func<ClickRecEventMsg, BaseReplyMsg> ClickEventHandler;

        /// <summary>
        /// 处理点击菜单跳转链接时的事件推送 
        /// </summary>
        protected event Func<ViewRecEventMsg, BaseReplyMsg> ViewEventHandler;

        #endregion

        /// <summary>
        /// 执行事件对应委托方法，如果对应的方法存在则执行
        /// </summary>
        /// <typeparam name="TRecMsg"></typeparam>
        /// <param name="res"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        private static BaseReplyMsg ExecuteHandler<TRecMsg>(TRecMsg res, Func<TRecMsg, BaseReplyMsg> func)
            where TRecMsg : BaseRecMsg, new()
        {
            var baseRep = func?.Invoke(res) ?? new NoneReplyMsg();

            baseRep.ToUserName = res.FromUserName;
            baseRep.FromUserName = res.ToUserName;
            baseRep.CreateTime = DateTime.Now.ToLocalSeconds();

            return baseRep;
        }

        #endregion

        #region 消息处理入口，出口（分为开始，处理，结束部分）

        /// <summary>
        /// 核心执行方法
        /// </summary>
        /// <param name="contentXml">内容信息</param>
        /// <param name="signature">签名信息</param>
        /// <param name="timestamp">时间戳</param>
        /// <param name="nonce">随机字符创</param>
        /// <param name="echostr">验证服务器参数，如果存在则只进行签名验证，并将在结果Data中返回</param>
        /// <returns>消息结果，Data为响应微信数据，如果出错Message为错误信息</returns>
        public ResultMo<string> Process(string contentXml, string signature, string timestamp, string nonce,
            string echostr)
        {
            // 一.  检查是否是服务器验证
            if (!string.IsNullOrEmpty(echostr))
            {
                return CheckServerValid(signature, timestamp, nonce, echostr);
            }

            // 二.  正常消息处理
            {
                var checkRes = ProcessBegin(contentXml, signature, timestamp, nonce);
                if (!checkRes.IsSuccess())
                    return checkRes.ConvertToResultOnly<string>();

                var contextRes = ProcessExecute(checkRes.data);
                if (!contextRes.IsSuccess())
                    return contextRes.ConvertToResultOnly<string>();

                ProcessEnd(contextRes.data);

                var resultString = contextRes.data.ReplyMsg.ToReplyXml();
                if (m_Config.SecurityType != WxSecurityType.None &&
                     !string.IsNullOrEmpty(contextRes.data.ReplyMsg.MsgType))
                {
                    return WxMsgHelper.EncryptMsg(resultString, m_Config);
                }
                return new ResultMo<string>(resultString);
            }
        }

        /// <summary>
        ///  服务器验证
        /// </summary>
        /// <param name="signature"></param>
        /// <param name="timestamp"></param>
        /// <param name="nonce"></param>
        /// <param name="echostr"></param>
        /// <returns></returns>
        public ResultMo<string> CheckServerValid(string signature, string timestamp, string nonce, string echostr)
        {
            var checkSignRes = WxMsgHelper.CheckSignature(m_Config.Token, signature, timestamp, nonce);
            var resultRes = checkSignRes.ConvertToResultOnly<string>();
            resultRes.data = resultRes.IsSuccess() ? echostr : string.Empty;
            return resultRes;
        }

        #endregion

        #region 消息处理 == start   验证消息参数以及加解密部分

        /// <summary>
        /// 核心执行方法    ==    验证签名和消息体信息解密处理部分
        /// </summary>
        /// <param name="recXml">消息内容</param>
        /// <param name="signature">微信加密签名</param>
        /// <param name="timestamp">时间戳</param>
        /// <param name="nonce">随机数</param>
        /// <returns>验证结果及相应的消息内容体 （如果加密模式，返回的是解密后的明文）</returns>
        protected ResultMo<string> ProcessBegin(string recXml, string signature,
            string timestamp, string nonce)
        {
            if (string.IsNullOrEmpty(recXml))
                return new ResultMo<string>(ResultTypes.ObjectNull, "接收的消息体为空！");

            var resCheck = WxMsgHelper.CheckSignature(m_Config.Token, signature, timestamp, nonce);
            if (!resCheck.IsSuccess())
                return resCheck.ConvertToResultOnly<string>();

            if (m_Config.SecurityType == WxSecurityType.None)
                return new ResultMo<string>(recXml);

            XmlDocument xmlDoc = null;
            var dirs = WxMsgHelper.ChangXmlToDir(recXml,ref xmlDoc);
            if (dirs == null || !dirs.ContainsKey("Encrypt"))
                return new ResultMo<string>(ResultTypes.ObjectNull, "加密消息为空");

            var recMsgXml = Cryptography.WxAesDecrypt(dirs["Encrypt"], m_Config.EncodingAesKey);
            return new ResultMo<string>(recMsgXml);
        }

        #endregion

        #region   消息处理 == Execute   处理消息传递响应

        /// <summary>
        /// 核心执行方法   ==  执行方法
        /// </summary>
        /// <param name="recMsgXml">传入消息的xml</param>
        private ResultMo<MsgContext> ProcessExecute(string recMsgXml)
        {
            XmlDocument xmlDoc = null;
            var recMsgDirs = WxMsgHelper.ChangXmlToDir(recMsgXml,ref xmlDoc);

            if (!recMsgDirs.ContainsKey("MsgType"))
                return new ResultMo<MsgContext>(ResultTypes.ParaError, "消息数据中未发现 消息类型（MsgType）字段！");

            var msgType = recMsgDirs["MsgType"].ToLower();
            if (msgType == "event")
            {
                if (!recMsgDirs.ContainsKey("Event"))
                    return new ResultMo<MsgContext>(ResultTypes.ParaError, "事件消息数据中未发现 事件类型（Event）字段！");
            }

            var context = ProcessExecute_BasicMsg(xmlDoc, msgType, recMsgDirs)
                          ?? ProcessExecute_CustomHandler(xmlDoc, msgType, recMsgDirs)
                          ?? ExecuteMsgHandler(xmlDoc, recMsgDirs,new BaseRecMsg(), UnknowHandler);

            return new ResultMo<MsgContext>(context);
        }

        #region  基础消息执行

        /// <summary>
        /// 执行高级消息事件类型
        /// </summary>
        /// <param name="recMsgXml">接收到的消息内容体</param>
        /// <param name="msgType">消息类型</param>
        /// <param name="msgDirs">消息内容体字典</param>
        /// <returns></returns>
        private MsgContext ProcessExecute_CustomHandler(XmlDocument recMsgXml, string msgType,
            Dictionary<string, string> msgDirs)
        {
            var key = msgType == "event" ? string.Concat("event_", msgDirs["Event"].ToLower()) : msgType;
            var handler = WxCustomHandlerProvider.GetHandler(key);

            if(handler==null)
                return null;  //  交由后续默认事件处理

            var recMsg = handler.CreateInstance();

            return ExecuteMsgHandler(recMsgXml, msgDirs, recMsg, handler.Excute);
        }

        /// <summary>
        ///  执行基础消息类型
        /// </summary>
        /// <param name="rMsg"></param>
        /// <param name="msgType"></param>
        /// <param name="rDirs"></param>
        /// <returns>返回基础消息处理结果</returns>
        private MsgContext ProcessExecute_BasicMsg(XmlDocument rMsg, string msgType,
            Dictionary<string, string> rDirs)
        {
            MsgContext context = null;
            switch (msgType.ToLower())
            {
                case "event":
                    context = ProcessExecute_BasicEventMsg(rMsg, rDirs);
                    break;
                case "text":
                    context = ExecuteMsgHandler(rMsg, rDirs,new TextRecMsg(), TextHandler);
                    break;
                case "image":
                    context = ExecuteMsgHandler(rMsg, rDirs,new ImageRecMsg(), ImageHandler);
                    break;
                case "voice":
                    context = ExecuteMsgHandler(rMsg, rDirs,new VoiceRecMsg(), VoiceHandler);
                    break;
                case "video":
                    context = ExecuteMsgHandler(rMsg, rDirs,new VideoRecMsg(), VideoHandler);
                    break;
                case "shortvideo":
                    context = ExecuteMsgHandler(rMsg, rDirs,new VideoRecMsg(), VideoHandler);
                    break;
                case "location":
                    context = ExecuteMsgHandler(rMsg, rDirs,new LocationRecMsg(), LocationHandler);
                    break;
                case "link":
                    context = ExecuteMsgHandler(rMsg, rDirs,new LinkRecMsg(), LinkHandler);
                    break;
            }
            return context;
        }


        /// <summary>
        ///  执行基础事件消息类型
        /// </summary>
        /// <param name="reMsg"></param>
        /// <param name="reDirs"></param>
        /// <returns>返回基础事件消息处理结果</returns>
        private MsgContext ProcessExecute_BasicEventMsg(XmlDocument reMsg, Dictionary<string, string> reDirs)
        {
            var eventType = reDirs["Event"].ToLower();
            MsgContext context = null;
            switch (eventType)
            {
                case "subscribe":
                    context = ExecuteMsgHandler(reMsg, reDirs,new SubscribeRecEventMsg(), SubscribeEventHandler);
                    break;
                case "unsubscribe":
                    context = ExecuteMsgHandler(reMsg, reDirs,new SubscribeRecEventMsg(), SubscribeEventHandler);
                    break;
                case "scan":
                    context = ExecuteMsgHandler(reMsg, reDirs,new SubscribeRecEventMsg(), ScanEventHandler);
                    break;
                case "location":
                    context = ExecuteMsgHandler(reMsg, reDirs,new LocationRecEventMsg(), LocationEventHandler);
                    break;
                case "click":
                    context = ExecuteMsgHandler(reMsg, reDirs,new ClickRecEventMsg(), ClickEventHandler);
                    break;
                case "view":
                    context = ExecuteMsgHandler(reMsg, reDirs,new ViewRecEventMsg(), ViewEventHandler);
                    break;
            }
            return context;
        }

        /// <summary>
        ///  根据具体的消息类型执行相关的消息委托方法(基础消息)
        /// </summary>
        /// <typeparam name="TRecMsg"></typeparam>
        /// <param name="recMsgXml"></param>
        /// <param name="recMsgDirs"></param>
        /// <param name="recMsg"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        private static MsgContext ExecuteMsgHandler<TRecMsg>(XmlDocument recMsgXml,
            IDictionary<string, string> recMsgDirs, TRecMsg recMsg, Func<TRecMsg, BaseReplyMsg> func)
            where TRecMsg : BaseRecMsg, new()
        {
            var msgContext = new MsgContext();

            recMsg.SetMsgDirs(recMsgDirs);
            recMsg.RecMsgXml = recMsgXml;

            msgContext.ReplyMsg = ExecuteHandler(recMsg, func);
            msgContext.RecMsg = recMsg;

            return msgContext;
        }


        #endregion

        #endregion

        #region  消息处理 == end  当前消息处理结束触发

        /// <summary>
        ///  执行结束方法
        /// </summary>
        /// <param name="msgContext"></param>
        protected virtual void ProcessEnd(MsgContext msgContext)
        {

        }

        #endregion
    }


    internal static class WxMsgHelper
    {
        #region   消息内容加解密辅助方法

        /// <summary>
        /// 验证签名方法
        /// </summary>
        /// <param name="token"></param>
        /// <param name="signature"></param>
        /// <param name="timestamp"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        internal static ResultMo CheckSignature(string token, string signature,
            string timestamp, string nonce)
        {
            return signature == GenerateSignature(token, timestamp, nonce)
                ? new ResultMo() 
                : new ResultMo(ResultTypes.UnAuthorize, "签名验证失败！");
        }


        /// <summary>
        /// 验证签名方法
        /// </summary>
        /// <param name="token"></param>
        /// <param name="timestamp"></param>
        /// <param name="nonce"></param>
        /// <param name="strEncryptMsg"></param>
        /// <returns></returns>
        internal static string GenerateSignature(string token,
            string timestamp, string nonce, string strEncryptMsg = "")
        {
            var strList = new List<string>() { token, timestamp, nonce, strEncryptMsg };
            strList.Sort();

            var waitEncropyStr = string.Join(string.Empty, strList);
            return Sha1.Encrypt(waitEncropyStr, Encoding.ASCII);
        }



        /// <summary>
        ///  加密消息体
        /// </summary>
        /// <param name="sReplyMsg"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        internal static ResultMo<string> EncryptMsg(string sReplyMsg, WxMsgServerConfig config)
        {
            string raw;
            try
            {
                raw = Cryptography.AesEncrypt(sReplyMsg, config.EncodingAesKey, config.AppId);
            }
            catch (Exception)
            {
                return new ResultMo<string>(ResultTypes.InnerError, "加密响应消息体出错！");
            }
            var date = DateTime.Now;

            var sTimeStamp = date.ToUtcSeconds().ToString();
            var sNonce = date.ToString("yyyyMMddHHssff");


            var msgSigature = GenerateSignature(config.Token, sTimeStamp, sNonce, raw);
            if (string.IsNullOrEmpty(msgSigature))
            {
                return new ResultMo<string>(ResultTypes.InnerError, "生成签名信息出错！");
            }

            var sEncryptMsg = new StringBuilder();

            const string EncryptLabelHead = "<Encrypt><![CDATA[";
            const string EncryptLabelTail = "]]></Encrypt>";
            const string MsgSigLabelHead = "<MsgSignature><![CDATA[";
            const string MsgSigLabelTail = "]]></MsgSignature>";
            const string TimeStampLabelHead = "<TimeStamp><![CDATA[";

            const string TimeStampLabelTail = "]]></TimeStamp>";
            const string NonceLabelHead = "<Nonce><![CDATA[";
            const string NonceLabelTail = "]]></Nonce>";

            sEncryptMsg.Append("<xml>").Append(EncryptLabelHead).Append(raw).Append(EncryptLabelTail);
            sEncryptMsg.Append(MsgSigLabelHead).Append(msgSigature).Append(MsgSigLabelTail);
            sEncryptMsg.Append(TimeStampLabelHead).Append(sTimeStamp).Append(TimeStampLabelTail);
            sEncryptMsg.Append(NonceLabelHead).Append(sNonce).Append(NonceLabelTail);
            sEncryptMsg.Append("</xml>");

            return new ResultMo<string>(sEncryptMsg.ToString());
        }


        #endregion

        #region  消息内容辅助类
        
        /// <summary>
        /// 把xml文本转化成字典对象
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="xmlDoc">返回格式化后的xml对象</param>
        /// <returns></returns>
        internal static Dictionary<string, string> ChangXmlToDir(string xml,ref XmlDocument xmlDoc)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }
            var dirs = new Dictionary<string, string>();

            xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);
            var xmlNode = xmlDoc.FirstChild;
            var nodes = xmlNode.ChildNodes;

            foreach (XmlNode xn in nodes)
            {
                var xe = (XmlElement)xn;
                dirs[xe.Name] = xe.InnerText;
            }

            return dirs;
        }
        #endregion
    }
}
