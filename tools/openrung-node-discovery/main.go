package main

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"time"

	"github.com/openrung/openrung/brokerapi"
	"github.com/openrung/openrung/connectcore"
)

type relayRow struct {
	Relay   brokerapi.RelayDescriptor `json:"relay"`
	ProbeMS *int64                    `json:"probe_ms,omitempty"`
}

func main() {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	engine := connectcore.New()
	rows, err := engine.RankedDirectory(ctx, "")
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	out := make([]relayRow, 0, len(rows))
	for _, row := range rows {
		out = append(out, relayRow{Relay: row.Relay, ProbeMS: row.ProbeMS})
	}
	enc := json.NewEncoder(os.Stdout)
	enc.SetEscapeHTML(true)
	if err := enc.Encode(out); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(3)
	}
}
