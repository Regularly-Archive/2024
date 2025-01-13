// content.js

// 标志：检查按钮是否已经插入
let buttonInserted = false;

// 创建按钮并插入到 OneTranscript 元素中
function insertSummarizeButton() {
    const transcriptContainer = document.querySelector('#OneTranscript');

    if (transcriptContainer && !buttonInserted) { // 确保按钮只插入一次
        // 创建按钮
        const summarizeButton = document.createElement('button');
        summarizeButton.innerText = '总结会议';

        // 美化按钮样式
        summarizeButton.style.padding = '10px 0';  // 上下10px，左右0px
        summarizeButton.style.width = '80px';  // 设置宽度为80px
        summarizeButton.style.fontSize = '14px'; // 字体大小
        summarizeButton.style.fontWeight = 'bold'; // 加粗
        summarizeButton.style.marginTop = '15px'; // 与上方的间距
        summarizeButton.style.marginLeft = '5px';
        summarizeButton.style.cursor = 'pointer';
        summarizeButton.style.backgroundColor = '#5b5fc7'; // 主色调背景
        summarizeButton.style.color = '#fff'; // 文字颜色
        summarizeButton.style.border = 'none'; // 去除边框
        summarizeButton.style.borderRadius = '40px'; // 圆角矩形（半圆角效果）
        summarizeButton.style.boxShadow = '0 4px 6px rgba(0, 0, 0, 0.1)'; // 轻微阴影
        summarizeButton.style.transition = 'all 0.3s ease'; // 过渡效果，平滑动画

        summarizeButton.addEventListener('click', () => {
            const transcripts = extractTranscripts()
            console.log(transcripts)
            // 你可以在这里调用其他函数来生成会议总结
            alert('会议总结功能即将实现!');
        });

        // 悬停效果
        summarizeButton.addEventListener('mouseenter', () => {
            summarizeButton.style.backgroundColor = '#4a4cbf'; // 悬停时背景色稍微变深
            summarizeButton.style.transform = 'scale(1.05)'; // 鼠标悬停时按钮变大
        });

        // 恢复到原来状态
        summarizeButton.addEventListener('mouseleave', () => {
            summarizeButton.style.backgroundColor = '#5b5fc7'; // 恢复原背景色
            summarizeButton.style.transform = 'scale(1)'; // 恢复原状态
        });

        // 按钮点击效果
        summarizeButton.addEventListener('mousedown', () => {
            summarizeButton.style.transform = 'scale(0.98)'; // 点击时缩小一点
        });

        summarizeButton.addEventListener('mouseup', () => {
            summarizeButton.style.transform = 'scale(1)'; // 松开点击后恢复
        });

        // 使按钮居中显示
        transcriptContainer.style.textAlign = 'center';  // 将 OneTranscript 内的文本对齐方式设置为居中
        transcriptContainer.appendChild(summarizeButton);

        // 更新按钮插入标志
        buttonInserted = true;
    }
}

function extractTranscripts() {
    let transcripts = []
    let regex = /(\d+)\s*分钟\s*(\d+)\s*秒/g
    let cells = document.querySelectorAll('.ms-List-cell')
    for (const cell of cells) {
        const speakerElement = cell.querySelector('span[itemprop="name"], span'); // 试图查找含有姓名的 span
        let speakerName = speakerElement ? speakerElement.innerText.trim() : '未知发言人';
        speakerName = speakerName.replace(regex,"")
      
        // 获取发言内容，通常在包含文本的 div 或 span 中
        const contentElement = cell.querySelector('div[role="group"], div[aria-labelledby], div[aria-describedby], div'); // 查找含有发言内容的 div
        const contentText = contentElement ? contentElement.innerText.split('\n').reverse()[0].trim() : '无内容';

        transcripts.push(`${speakerName}: ${contentText}`)
    }
    
    return transcripts.join('\n')
}

// 使用 IntersectionObserver 监听元素是否可见
function observeVisibility() {
    const transcriptContainer = document.querySelector('#OneTranscript');

    if (transcriptContainer) {
        const observer = new IntersectionObserver((entries) => {
            entries.forEach((entry) => {
                if (entry.isIntersecting && !buttonInserted) {  // 检查按钮是否已插入
                    // 一旦 OneTranscript 元素可见，插入按钮
                    insertSummarizeButton();
                    observer.disconnect();  // 元素可见时停止观察
                }
            });
        }, { threshold: 0.1 }); // 当元素至少 10% 可见时触发

        // 开始观察 OneTranscript 元素
        observer.observe(transcriptContainer);
    }
}

// 使用 MutationObserver 监听 OneTranscript 元素的插入
function observeTranscriptLoad() {
    const observer = new MutationObserver((mutationsList) => {
        mutationsList.forEach((mutation) => {
            if (mutation.type === 'childList') {
                const transcriptContainer = document.querySelector('#OneTranscript');
                if (transcriptContainer && !buttonInserted) {
                    // 如果 OneTranscript 元素已经出现在 DOM 中且按钮没有插入
                    observeVisibility();
                    observer.disconnect(); // 停止观察
                }
            }
        });
    });

    // 观察整个 body 元素的变化，特别是子元素的变化
    observer.observe(document.body, {
        childList: true,
        subtree: true
    });
}

// 等待页面加载完成后执行
window.addEventListener('load', () => {
    observeTranscriptLoad();  // 启动观察器来监听 OneTranscript 的加载
});
