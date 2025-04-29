using BBDown.Core.Entity;
using System.Text.Json;
using System.Diagnostics;
using static BBDown.Core.Entity.Entity;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Logger;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 收藏夹解析
/// https://space.bilibili.com/3/favlist
///
/// </summary>
public class FavListFetcher : IFetcher
{
    public void ExceptionWithDiagnosticsLog(String msg = "") {
      StackFrame stackFrame = new StackTrace(new StackFrame(1,true)).GetFrame(0);
      LogError($"Exception at {stackFrame.GetFileName()}:{stackFrame.GetFileLineNumber()}\n\tMethod {stackFrame.GetMethod().Name}, {msg}");
    }

    public JsonElement? TryGetProperty(JsonElement e, String name) {
      if (e == null) return null; 
      try {
        return e!.Value.GetProperty(name);
      } catch (InvalidOperationException ex) {
        ExceptionWithDiagnosticsLog($"Could not get property {name}");
        return null;
      }
    }
    public String GetPropertyToString(JsonElement? e, String name, String defaultValue = "") {
      if (e == null) return defaultValue; 
      try {
        return e!.Value.GetProperty(name).GetString();
      } catch (InvalidOperationException ex) {
        ExceptionWithDiagnosticsLog($"Could not get property {name}");
        return defaultValue;
      }
    }

    public Int32 GetPropertyToInt32(JsonElement? e, String name, Int32 defaultValue = 0) {
      if (e == null) return defaultValue; 
      try {
        return e!.Value.GetProperty(name).GetInt32();
        ExceptionWithDiagnosticsLog($"Could not get property {name}");
      } catch (InvalidOperationException ex) {
        return defaultValue;
      }
    }

    public Int64 GetPropertyToInt64(JsonElement? e, String name, Int64 defaultValue = 0) {
      if (e == null) return defaultValue; 
      try {
        return e!.Value.GetProperty(name).GetInt64();
      } catch (InvalidOperationException ex) {
        ExceptionWithDiagnosticsLog($"Could not get property {name}");
        return defaultValue;
      }
    }
    
    public async Task<VInfo> FetchAsync(string id)
    {
        id = id[6..];
        var favId = id.Split(':')[0];
        var mid = id.Split(':')[1];
        //查找默认收藏夹
        if (favId == "")
        {
            var favListApi = $"https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid={mid}";
            favId = JsonDocument.Parse(await GetWebSourceAsync(favListApi)).RootElement.GetProperty("data").GetProperty("list").EnumerateArray().First().GetProperty("id").ToString();
        }

        int pageSize = 20;
        int index = 1;
        List<Page> pagesInfo = new();

        var api = $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={favId}&pn=1&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
        var json = await GetWebSourceAsync(api);
        using var infoJson = JsonDocument.Parse(json);
        var data = infoJson.RootElement.GetProperty("data");
        int totalCount = data.GetProperty("info").GetProperty("media_count").GetInt32();
        int totalPage = (int)Math.Ceiling((double)totalCount / pageSize);
        var title = data.GetProperty("info").GetProperty("title").GetString()!;
        var intro = data.GetProperty("info").GetProperty("intro").GetString()!;
        long pubTime = data.GetProperty("info").GetProperty("ctime").GetInt64();
        var userName = data.GetProperty("info").GetProperty("upper").GetProperty("name").ToString();
        var medias = data.GetProperty("medias").EnumerateArray().ToList();
        int err_count = 0;
        
        for (int page = 2; page <= totalPage; page++)
        {
            api = $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={favId}&pn={page}&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
            json = await GetWebSourceAsync(api);
            var jsonDoc = JsonDocument.Parse(json);
            try {
                data = jsonDoc.RootElement.GetProperty("data");
                medias.AddRange(data.GetProperty("medias").EnumerateArray().ToList());
            } catch (InvalidOperationException e) {
                err_count++;
                LogError($"错误发生于: 标题:{title},目标api:{api},内容为:{json}");
                if (err_count >= 5) {
                    LogError("错误仍然无法恢复!");
                    throw e;
                } else {
                    LogWarn("执行跳过...");
                    continue;
                }
            }
        }
        err_count = 0;

        foreach (var m in medias)
        {
            //只处理视频类型(可以直接在query param上指定type=2)
            // if (m.GetProperty("type").GetInt32() != 2) continue;
            //只处理未失效视频
            if (m.GetProperty("attr").GetInt32() != 0) continue;

            var pageCount = m.GetProperty("page").GetInt32();
            if (pageCount > 1)
            {
                var tmpInfo = await new NormalInfoFetcher().FetchAsync(m.GetProperty("id").ToString());
                foreach (var item in tmpInfo.PagesInfo)
                {
                    Page p = new(index++, item)
                    {
                        title = m.GetProperty("title").ToString() + $"_P{item.index}_{item.title}",
                        cover = tmpInfo.Pic,
                        desc = m.GetProperty("intro").ToString()
                    };
                    if (!pagesInfo.Contains(p)) pagesInfo.Add(p);
                }
            }
            else
            {

                    var id_ = GetPropertyToString(m, "id");
                    if (String.IsNullOrEmpty(id_)) {
                        LogError("致命错误，无法获取id.跳过...");
                        continue;
                    }
                    var e_tmp_  = TryGetProperty(m, "ugc");
                    if (e_tmp_ == null) {
                        LogError($"致命错误，目标{id_}无法获取first_cid");
                    }
                    var cid_ = GetPropertyToString(e_tmp_, "first_cid");
                    var epid_ = ""; //epid
                    var title_ = GetPropertyToString(m, "title");
                    var dur_ = GetPropertyToInt32(m, "duration");
                    var u_ = "";
                    var pubtime_ = GetPropertyToInt64(m, "pubtime");
                    var cover_ = GetPropertyToString(m, "cover");
                    var intro_ = GetPropertyToString(m, "intro");
                    e_tmp_  = TryGetProperty(m, "upper");
                    var upper_name_ = GetPropertyToString(e_tmp_, "name");
                    var upper_mid_ = GetPropertyToString(e_tmp_, "mid");
                    Page p = new(index++, id_, cid_, epid_, title_, dur_, u_, pubtime_, cover_, intro_, upper_name_, upper_mid_);
                    if (!pagesInfo.Contains(p)) pagesInfo.Add(p);
            
            }
        }

        var info = new VInfo
        {
            Title = title.Trim(),
            Desc = intro.Trim(),
            Pic = "",
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = false
        };

        return info;
    }
}
