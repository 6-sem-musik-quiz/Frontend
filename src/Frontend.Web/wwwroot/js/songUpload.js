// Uploads the file selected in an <input type="file"> as multipart/form-data with XMLHttpRequest.
// XHR is used instead of fetch because only XHR reports upload progress. The browser streams the
// File straight from disk, so a large mp3 is never copied into .NET memory.
//
// fields: { name: value | value[] } for the text fields (an array is sent as a repeated field).
// progress: a .NET object reference with [JSInvokable] OnProgress(loaded, total).
// Resolves with { status, contentType, body } for every HTTP answer; status 0 = network error.
export function upload(url, token, input, fields, progress) {
    return new Promise((resolve) => {
        const file = input?.files?.[0];
        if (!file) {
            resolve({ status: -1, contentType: "", body: "" });
            return;
        }

        const form = new FormData();
        for (const [name, value] of Object.entries(fields)) {
            for (const v of Array.isArray(value) ? value : [value]) {
                form.append(name, String(v));
            }
        }
        form.append("file", file, file.name);

        const xhr = new XMLHttpRequest();
        xhr.open("POST", url);
        if (token) {
            xhr.setRequestHeader("Authorization", "Bearer " + token);
        }

        let lastReported = -1;
        xhr.upload.onprogress = (e) => {
            if (!e.lengthComputable || !progress) return;
            const percent = Math.floor((e.loaded / e.total) * 100);
            if (percent !== lastReported) {
                lastReported = percent;
                progress.invokeMethodAsync("OnProgress", e.loaded, e.total);
            }
        };

        const done = () => resolve({
            status: xhr.status,
            contentType: xhr.getResponseHeader("Content-Type") ?? "",
            body: xhr.responseText ?? ""
        });
        xhr.onload = done;
        xhr.onerror = done;   // status 0
        xhr.onabort = done;   // status 0
        xhr.send(form);
    });
}
