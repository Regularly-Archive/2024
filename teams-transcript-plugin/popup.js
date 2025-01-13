// popup.js

document.getElementById('summarizeBtn').addEventListener('click', () => {
    chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
      chrome.tabs.sendMessage(tabs[0].id, { action: 'summarizeTranscript' }, (response) => {
        if (response.transcript) {
          // 发送摘要请求（你可以在这里使用 OpenAI API 或其他 NLP 工具生成摘要）
          summarizeText(response.transcript);
        } else {
          document.getElementById('summaryResult').innerText = '未找到会议脚本或发生错误';
        }
      });
    });
  });
  
  function summarizeText(transcript) {
    // 这里你可以调用 NLP API 生成会议总结
    // 例如使用 OpenAI API 或其他本地处理方法来创建摘要
    document.getElementById('summaryResult').innerText = '正在生成摘要...';
  
    // 假设调用 API 成功并返回摘要（这里只是一个示例）
    setTimeout(() => {
      document.getElementById('summaryResult').innerText = '会议总结：\n' + transcript.slice(0, 300) + '...'; // 示例，实际上需要对文本做处理
    }, 2000);
  }
  