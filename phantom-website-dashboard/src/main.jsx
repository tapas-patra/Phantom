import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import "./styles.css";

class ApplicationErrorBoundary extends React.Component {
  constructor(props) {
    super(props);
    this.state = { failed: false };
  }

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error, errorInfo) {
    console.error("Phantom dashboard render failed", error, errorInfo);
  }

  render() {
    if (this.state.failed) {
      return (
        <main className="page">
          <section className="glass-panel page-intro">
            <p className="eyebrow">Phantom</p>
            <h1>This page could not be displayed.</h1>
            <p>Reload the page to try again. If the problem continues, share the time of the error with support.</p>
            <button className="button button-primary" type="button" onClick={() => window.location.reload()}>Reload page</button>
          </section>
        </main>
      );
    }

    return this.props.children;
  }
}

ReactDOM.createRoot(document.getElementById("root")).render(
  <React.StrictMode>
    <ApplicationErrorBoundary>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ApplicationErrorBoundary>
  </React.StrictMode>
);
