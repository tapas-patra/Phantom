import SwiftUI
import WebKit

struct MermaidDiagram: NSViewRepresentable {
    let source: String
    let onRenderFailed: (Bool) -> Void

    final class Coordinator: NSObject, WKScriptMessageHandler {
        var source = ""
        var onRenderFailed: (Bool) -> Void

        init(onRenderFailed: @escaping (Bool) -> Void) {
            self.onRenderFailed = onRenderFailed
        }

        func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
            guard let state = message.body as? String else { return }
            DispatchQueue.main.async { self.onRenderFailed(state == "failed") }
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator(onRenderFailed: onRenderFailed) }

    func makeNSView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        configuration.userContentController.add(context.coordinator, name: "mermaid")
        let view = WKWebView(frame: .zero, configuration: configuration)
        view.setValue(false, forKey: "drawsBackground")
        load(source, in: view, coordinator: context.coordinator)
        return view
    }

    func updateNSView(_ view: WKWebView, context: Context) {
        context.coordinator.onRenderFailed = onRenderFailed
        guard context.coordinator.source != source else { return }
        load(source, in: view, coordinator: context.coordinator)
    }

    static func dismantleNSView(_ view: WKWebView, coordinator: Coordinator) {
        view.configuration.userContentController.removeScriptMessageHandler(forName: "mermaid")
    }

    private func load(_ source: String, in view: WKWebView, coordinator: Coordinator) {
        coordinator.source = source
        let script = Bundle.main.url(forResource: "mermaid.min", withExtension: "js")
        view.loadHTMLString(Self.html(source, hasLocalRuntime: script != nil), baseURL: script?.deletingLastPathComponent())
    }

    static func html(_ raw: String, hasLocalRuntime: Bool) -> String {
        struct Payload: Encodable {
            let raw: String
            let candidates: [String]
        }

        let payload = Payload(raw: normalized(raw), candidates: renderCandidates(raw))
        let data = try? JSONEncoder().encode(payload)
        let json = data.flatMap { String(data: $0, encoding: .utf8) } ?? #"{"raw":"","candidates":[]}"#
        let script = hasLocalRuntime ? #"<script src="mermaid.min.js"></script>"# : ""
        return """
        <!doctype html>
        <meta charset="utf-8">
        <style>
          html,body{margin:0;background:transparent;color:#e9eef6;font:13px -apple-system;overflow:auto}
          #diagram{padding:12px;min-width:max-content}#diagram svg{display:block;height:auto;max-width:none}
          #fallback{display:none;margin:0;padding:12px;white-space:pre;color:#00ff7f}
        </style>
        <div id="diagram"></div><pre id="fallback"></pre>
        \(script)
        <script>
          const payload=\(json),target=document.getElementById('diagram'),fallback=document.getElementById('fallback');
          const notify=state=>{try{window.webkit.messageHandlers.mermaid.postMessage(state)}catch{}};
          const fail=()=>{target.replaceChildren();fallback.textContent='Diagram could not be rendered.\\n\\n```mermaid\\n'+payload.raw+'\\n```';fallback.style.display='block';notify('failed')};
          (async()=>{
            if(!window.mermaid){fail();return}
            mermaid.initialize({startOnLoad:false,theme:'dark',securityLevel:'strict',suppressErrorRendering:true});
            for(let i=0;i<payload.candidates.length;i++){
              try{
                const candidate=payload.candidates[i];
                if(!await mermaid.parse(candidate,{suppressErrors:true}))continue;
                const {svg}=await mermaid.render('phantom-mermaid-'+i,candidate);
                if(/syntax error|parse error|mermaid version/i.test(svg))continue;
                target.innerHTML=svg;
                if(target.querySelector('svg')){notify('rendered');return}
              }catch{}
            }
            fail();
          })();
        </script>
        """
    }

    static func renderCandidates(_ raw: String) -> [String] {
        let source = normalized(raw)
        guard !source.isEmpty else { return [] }
        var candidates = [source]
        let header = recognizedHeaders.first { source.lowercased().hasPrefix($0.lowercased() + " ") }
        if let header, !source.hasPrefix(header + "\n") {
            candidates.append(header + "\n" + source.dropFirst(header.count).trimmingCharacters(in: .whitespacesAndNewlines))
        } else if !recognizedHeaders.contains(where: { source.lowercased() == $0.lowercased() || source.lowercased().hasPrefix($0.lowercased() + "\n") }) {
            candidates.append("flowchart TD\n" + source)
        }
        guard candidates.last?.lowercased().hasPrefix("flowchart ") == true || candidates.last?.lowercased().hasPrefix("graph ") == true else { return candidates }
        let repaired = candidates.last!
            .replacingOccurrences(of: #"\s*;\s*"#, with: "\n", options: .regularExpression)
            .replacingOccurrences(of: #"(?<=[\]\)\}])\s+(?=[A-Za-z][A-Za-z0-9_-]*\s*[-.=ox<>]{2,})"#, with: "\n", options: .regularExpression)
        if repaired != candidates.last { candidates.append(repaired) }
        return candidates
    }

    private static func normalized(_ raw: String) -> String {
        raw.replacingOccurrences(of: "\r\n", with: "\n")
            .replacingOccurrences(of: "\r", with: "\n")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static let recognizedHeaders = [
        "flowchart TB", "flowchart TD", "flowchart BT", "flowchart RL", "flowchart LR",
        "graph TB", "graph TD", "graph BT", "graph RL", "graph LR",
        "sequenceDiagram", "classDiagram", "stateDiagram-v2", "stateDiagram", "erDiagram",
        "journey", "gantt", "pie", "gitGraph", "mindmap", "timeline", "quadrantChart",
        "requirementDiagram", "xychart-beta"
    ]
}
